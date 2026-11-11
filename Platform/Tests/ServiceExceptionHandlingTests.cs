using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DisputePortal.Platform.Contracts;
using DisputePortal.Platform.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;
using DisputePortal.Platform.Infrastructure.Models;

namespace DisputePortal.Platform.Tests;

public class ServiceExceptionHandlingTests
{
    [Fact]
    public async Task Native_handler_preserves_service_error_contract_and_sanitizes_infrastructure_failures()
    {
        var logs = new ErrorLog();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" });
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services.AddProblemDetails();
        builder.Services.AddExceptionHandler<ServiceExceptionHandler>();
        builder.Services.AddSingleton<ILogger<ServiceExceptionHandler>>(logs);
        await using var app = builder.Build();
        var correlation = Guid.NewGuid().ToString("D");
        app.Use((context, next) =>
        {
            context.Items["CorrelationId"] = correlation;
            return next(context);
        });
        app.UseExceptionHandler();
        app.MapGet(
            "/known",
            (Func<IResult>)(() => throw new PlatformException(409, "already_processed", "The request was already processed.")));
        app.MapGet("/infrastructure", (Func<IResult>)(() => throw new InvalidOperationException("password=private-database-secret")));
        await app.StartAsync();
        var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        using var client = new HttpClient
        {
            BaseAddress = new Uri(address)
        };
        using var known = await client.GetAsync("/known");
        Assert.Equal(HttpStatusCode.Conflict, known.StatusCode);
        Assert.Equal("application/problem+json", known.Content.Headers.ContentType!.MediaType);
        var contract = await known.Content.ReadFromJsonAsync<ServiceProblem>();
        Assert.Equal("already_processed", contract!.Code);
        Assert.Equal("The request was already processed.", contract.Message);
        Assert.Empty(logs.Errors);
        using var failure = await client.GetAsync("/infrastructure");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, failure.StatusCode);
        var body = await failure.Content.ReadAsStringAsync();
        Assert.DoesNotContain("private-database-secret", body);
        Assert.DoesNotContain("InvalidOperationException", body);
        using var document = JsonDocument.Parse(body);
        Assert.Equal("service_unavailable", document.RootElement.GetProperty("code").GetString());
        Assert.Equal(correlation, document.RootElement.GetProperty("correlationId").GetString());
        Assert.IsType<InvalidOperationException>(Assert.Single(logs.Errors));
        await app.StopAsync();
    }

    [Fact]
    public async Task Handler_does_not_write_to_an_aborted_request()
    {
        var services = new ServiceCollection().AddLogging().AddProblemDetails().BuildServiceProvider();
        await using var lifetime = services;
        var logs = new ErrorLog();
        var handler = new ServiceExceptionHandler(logs, services.GetRequiredService<IProblemDetailsService>());
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        var context = new DefaultHttpContext
        {
            RequestAborted = canceled.Token
        };
        Assert.False(await handler.TryHandleAsync(context, new OperationCanceledException(canceled.Token), canceled.Token));
        Assert.Empty(logs.Errors);
    }

    private class ErrorLog : ILogger<ServiceExceptionHandler>
    {
        public ConcurrentQueue<Exception> Errors { get; } = new();

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel level) => true;

        public void Log<TState>(LogLevel level, EventId id, TState state, Exception? error, Func<TState, Exception?, string> formatter)
        {
            if (level >= LogLevel.Error && error is not null)
            {
                Errors.Enqueue(error);
            }
        }
    }
}
