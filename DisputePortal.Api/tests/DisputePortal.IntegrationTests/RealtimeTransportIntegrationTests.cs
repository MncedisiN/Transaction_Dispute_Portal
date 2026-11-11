using DisputePortal.TestFixtures;
using System.Net;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using DisputePortal.Api.Middleware;
using DisputePortal.Api.Realtime;
using DisputePortal.Api.Service.Security;
using DisputePortal.Api.Models.Internal;
using DisputePortal.Api.Models.Responses;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using DisputePortal.Api.Interfaces.Repositories;
using DisputePortal.Api.Models.Domain;
using DisputePortal.Api.Interfaces.Security;
using DisputePortal.Api.Interfaces.Services;

namespace DisputePortal.IntegrationTests;

[CollectionDefinition("Realtime transport", DisableParallelization = true)]
public sealed class RealtimeTransportCollection
{
}

[Collection("Realtime transport")]
public sealed class RealtimeTransportIntegrationTests
{
    private const string AllowedOrigin = "https://portal.example.test";

    [Fact]
    public async Task Cookie_csrf_origin_and_websocket_protocol_protect_actual_changed_delivery_and_session_abort()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var ct = deadline.Token;
        var state = new TransportIdentity();
        var ready = new ConnectionReady();
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing",
            ContentRootPath = Path.GetTempPath()
        });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(
            IPAddress.Loopback,
            0,
            listener => listener.Protocols = HttpProtocols.Http1));
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Authentication:Mode"] = "Cookie",
            ["AllowedOrigins:0"] = AllowedOrigin
        });
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder.Services.AddSingleton<IAuthenticationRepository>(state);
        builder.Services.AddSingleton<IRealtimeSessionReader>(state);
        builder.Services.AddSingleton(TestRuntimeConfiguration.Read());
        builder.Services.AddSingleton<RealtimeConnections>();
        builder.Services.AddSingleton<IRealtimePublisher, RealtimePublisher>();
        builder.Services.AddSingleton(ready);
        builder.Services.AddSingleton<ReadyFilter>();
        builder.Services.AddSignalR(options =>
        {
            options.MaximumReceiveMessageSize = 4096;
            options.MaximumParallelInvocationsPerClient = 1;
            options.HandshakeTimeout = TimeSpan.FromSeconds(10);
        }).AddHubOptions<PortalRealtimeHub>(options => options.AddFilter<ReadyFilter>());
        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
        {
            options.Cookie.Name = "resolve.realtime-integration.auth";
            options.Cookie.SecurePolicy = CookieSecurePolicy.None;
            options.SlidingExpiration = false;
            options.Events.OnValidatePrincipal = PortalCookieSessionValidation.ValidateCookieSession;
            options.Events.OnRedirectToLogin = context =>
            {
                context.Response.StatusCode = 401;
                return Task.CompletedTask;
            };
        });
        builder.Services.AddAuthorization();
        builder.Services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-CSRF-TOKEN";
            options.Cookie.Name = "resolve.realtime-integration.csrf";
            options.Cookie.SecurePolicy = CookieSecurePolicy.None;
        });
        builder.Services.AddRateLimiter(options => options.AddConcurrencyLimiter(
            "realtime",
            limits =>
        {
            limits.PermitLimit = 4;
            limits.QueueLimit = 0;
        }));
        builder.Services.AddProblemDetails();
        await using var app = builder.Build();
        app.UseRouting();
        app.UseWebSockets();
        app.UseMiddleware<RealtimeOriginMiddleware>();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseRateLimiter();
        app.UseAntiforgery();
        app.UseMiddleware<CsrfValidationMiddleware>();
        // Fixture-only cookie issuance, not an endpoint or MFA option in the real API.
        app.MapGet(
            "/test/session",
            async (HttpContext context) =>
        {
            var principal = new ClaimsPrincipal(new ClaimsIdentity(
                    [new Claim(ClaimTypes.NameIdentifier, state.UserId.ToString()), new Claim("session_id", state.SessionId.ToString()), new Claim(
                ClaimTypes.Role,
                "customer")],
                    CookieAuthenticationDefaults.AuthenticationScheme));
            await context.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    principal,
                    new AuthenticationProperties { ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(1) });
            return Results.NoContent();
        });
        app.MapGet(
            "/api/v1/auth/csrf",
            (HttpContext context, IAntiforgery antiforgery) => Results.Json(new { token = antiforgery.GetAndStoreTokens(context).RequestToken }));
        app.MapHub<PortalRealtimeHub>(
            "/api/v1/realtime",
            options =>
        {
            options.Transports = HttpTransportType.WebSockets;
            options.CloseOnAuthenticationExpiration = true;
            options.ApplicationMaxBufferSize = 16 * 1024;
            options.TransportMaxBufferSize = 16 * 1024;
        }).RequireAuthorization().RequireRateLimiting("realtime").WithMetadata(new Microsoft.AspNetCore.Antiforgery.RequireAntiforgeryTokenAttribute());
        await app.StartAsync(ct);
        try
        {
            var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var handler = new HttpClientHandler
            {
                CookieContainer = new CookieContainer(),
                AllowAutoRedirect = false
            };
            using var http = new HttpClient(handler)
            {
                BaseAddress = new Uri(address)
            };
            var anonymousToken = await Csrf(http, ct);
            using (var denied = await Negotiate(http, anonymousToken, ct))
                Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode);
            using (var signedIn = await http.GetAsync("/test/session", ct))
                signedIn.EnsureSuccessStatusCode();
            using (var missingCsrf = await Negotiate(http, null, ct))
                Assert.Equal(HttpStatusCode.BadRequest, missingCsrf.StatusCode);
            var token = await Csrf(http, ct);
            using var negotiation = await Negotiate(http, token, ct);
            negotiation.EnsureSuccessStatusCode();
            using var details = JsonDocument.Parse(await negotiation.Content.ReadAsStringAsync(ct));
            var connectionToken = details.RootElement.GetProperty("connectionToken").GetString()!;
            var socketUri = new Uri(address.Replace("http://", "ws://", StringComparison.Ordinal) + "/api/v1/realtime?id=" + Uri.EscapeDataString(connectionToken));
            using (var forged = new HttpRequestMessage(
                HttpMethod.Get,
                socketUri.AbsoluteUri.Replace("ws://", "http://", StringComparison.Ordinal)))
            {
                forged.Headers.Add("Origin", "https://forged.example.test");
                forged.Headers.TryAddWithoutValidation("Connection", "Upgrade");
                forged.Headers.TryAddWithoutValidation("Upgrade", "websocket");
                forged.Headers.TryAddWithoutValidation("Sec-WebSocket-Version", "13");
                forged.Headers.TryAddWithoutValidation("Sec-WebSocket-Key", Convert.ToBase64String(new byte[16]));
                using var rejected = await http.SendAsync(forged, ct);
                Assert.Equal(HttpStatusCode.Forbidden, rejected.StatusCode);
            }

            using var socket = new ClientWebSocket();
            socket.Options.Cookies = handler.CookieContainer;
            socket.Options.SetRequestHeader("Origin", AllowedOrigin);
            await socket.ConnectAsync(socketUri, ct);
            await socket.SendAsync(
                new ArraySegment<byte>(Encoding.UTF8.GetBytes("{\"protocol\":\"json\",\"version\":1}\u001e")),
                WebSocketMessageType.Text,
                true,
                ct);
            var records = new JsonRecords(socket);
            Assert.Equal("{}", await records.Read(ct));
            await ready.Completion.Task.WaitAsync(ct);
            var resource = Guid.NewGuid();
            await app.Services.GetRequiredService<IRealtimePublisher>().PublishPortalUpdate(
                [state.UserId],
                false,
                ["disputes"],
                resource,
                ct);
            using (var changed = JsonDocument.Parse((await records.Read(ct))!))
            {
                Assert.Equal(1, changed.RootElement.GetProperty("type").GetInt32());
                Assert.Equal("Changed", changed.RootElement.GetProperty("target").GetString());
                var payload = changed.RootElement.GetProperty("arguments")[0];
                Assert.Equal("disputes", payload.GetProperty("topics")[0].GetString());
                Assert.Equal(resource, payload.GetProperty("resourceId").GetGuid());
                Assert.True(payload.TryGetProperty("occurredAt", out _));
                Assert.False(payload.TryGetProperty("body", out _));
            }

            Assert.Equal(0, state.Touches);
            state.Active = false;
            var registry = app.Services.GetRequiredService<RealtimeConnections>();
            var worker = new RealtimeSessionWorker(
                registry,
                app.Services.GetRequiredService<IServiceScopeFactory>(),
                NullLogger<RealtimeSessionWorker>.Instance,
                TestRuntimeConfiguration.Read(),
                app.Services.GetRequiredService<IHubContext<PortalRealtimeHub>>());
            await worker.ValidateConnections(state, state, ct);
            Assert.Empty(registry.Snapshot());
            try
            {
                var ended = await records.Read(ct);
                if (ended is not null)
                {
                    using var close = JsonDocument.Parse(ended);
                    Assert.True(close.RootElement.GetProperty("type").GetInt32() == 7
                        || close.RootElement.GetProperty("target").GetString() == "SessionEnded");
                }
            }
            catch (WebSocketException)
            { /* Context.Abort may close TCP without a WebSocket close frame. */
            }
        }
        finally
        {
            await app.StopAsync(CancellationToken.None);
        }
    }

    private static async Task<string> Csrf(HttpClient http, CancellationToken ct)
    {
        using var response = await http.GetAsync("/api/v1/auth/csrf", ct);
        response.EnsureSuccessStatusCode();
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return json.RootElement.GetProperty("token").GetString()!;
    }

    private static Task<HttpResponseMessage> Negotiate(HttpClient http, string? token, CancellationToken ct)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/realtime/negotiate?negotiateVersion=1");
        request.Headers.Add("Origin", AllowedOrigin);
        if (token is not null)
        {
            request.Headers.Add("X-CSRF-TOKEN", token);
        }

        return Send();
        async Task<HttpResponseMessage> Send()
        {
            using (request)
                return await http.SendAsync(request, ct);
        }
    }

    private sealed class ConnectionReady
    {
        public TaskCompletionSource<bool> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class ReadyFilter : IHubFilter
    {
        private readonly ConnectionReady ready;

        public ReadyFilter(ConnectionReady ready) => this.ready = ready;

        public async Task OnConnectedAsync(HubLifetimeContext context, Func<HubLifetimeContext, Task> next)
        {
            await next(context);
            ready.Completion.TrySetResult(true);
        }
    }

    private sealed class JsonRecords(ClientWebSocket socket)
    {
        private readonly StringBuilder pending = new();

        public async Task<string?> Read(CancellationToken ct)
        {
            var buffer = new byte[8192];
            while (true)
            {
                var separator = pending.ToString().IndexOf('\u001e');
                if (separator >= 0)
                {
                    var record = pending.ToString(0, separator);
                    pending.Remove(0, separator + 1);
                    using var json = JsonDocument.Parse(record);
                    if (json.RootElement.TryGetProperty("type", out var type) && type.GetInt32() == 6)
                    {
                        continue;
                    }

                    return record;
                }

                var received = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                if (received.MessageType == WebSocketMessageType.Close)
                {
                    return null;
                }

                pending.Append(Encoding.UTF8.GetString(buffer, 0, received.Count));
                if (pending.Length > 16 * 1024)
                {
                    throw new InvalidOperationException("Unexpected oversized hub test record.");
                }
            }
        }
    }

    private sealed class TransportIdentity : IAuthenticationRepository, IRealtimeSessionReader
    {
        public Guid UserId { get; } = Guid.NewGuid();
        public Guid SessionId { get; } = Guid.NewGuid();

        public volatile bool Active = true;
        public int Touches { get; private set; }

        public Task<bool> IsActive(Guid user, Guid session, DateTimeOffset now, CancellationToken ct) =>
            Task.FromResult(Active && user == UserId && session == SessionId);

        public Task<CurrentUserDto?> GetCurrentUser(Guid userId, CancellationToken ct) =>
            Task.FromResult<CurrentUserDto?>(Active && userId == UserId ? new CurrentUserDto(
            UserId,
            "Transport fixture",
            "",
            ["customer"],
            []) : null);

        public Task<bool> IsSessionActive(Guid session, DateTimeOffset now, CancellationToken ct)
        {
            Touches++;
            return Task.FromResult(Active && session == SessionId);
        }

        public Task<PasswordState?> GetPasswordState(Guid user, CancellationToken ct) => Task.FromResult<PasswordState?>(null);

        public Task<LoginRecord?> FindLogin(string identifier, CancellationToken ct) => Task.FromResult<LoginRecord?>(null);

        public Task<Guid> CreateSession(Guid user, DateTimeOffset expiresAt, CancellationToken ct) => Task.FromResult(SessionId);

        public Task RevokeSession(Guid session, DateTimeOffset now, CancellationToken ct)
        {
            Active = false;
            return Task.CompletedTask;
        }
    }
}
