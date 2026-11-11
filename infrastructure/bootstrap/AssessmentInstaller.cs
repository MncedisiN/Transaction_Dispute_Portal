using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Identity;

namespace DisputePortal.Setup;

public static class AssessmentInstaller
{
    private static readonly string[] ServiceNames =
    [
        "identity", "configuration", "accounts", "ledger", "transactions", "payments",
        "notifications", "audit", "reporting", "scheduling", "debt"
    ];

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static InstallationResult Initialize(InstallationOptions options)
    {
        if (options.ApiDatabaseProvider is not ("SqlServer" or "PostgreSql"))
        {
            throw new InvalidOperationException("Choose SqlServer or PostgreSql for the API database.");
        }

        if (!Uri.TryCreate(options.PublicOrigin, UriKind.Absolute, out var origin)
            || origin.Scheme is not ("http" or "https")
            || (origin.Host is not ("localhost" or "127.0.0.1" or "[::1]")
                && !(origin.Scheme == "https" && origin.Host == options.LocalAddress
                    && LocalHttpsCertificates.IsPrivateAddress(origin.Host)))
            || origin.AbsolutePath != "/"
            || origin.UserInfo.Length > 0 || origin.Query.Length > 0 || origin.Fragment.Length > 0)
        {
            throw new InvalidOperationException("The assessment portal requires a loopback origin or HTTPS at the configured private address.");
        }

        var messaging = ReadMessagingSettings(options.PrivateSettingsFile);
        var pushSubject = options.PushSubject;
        if (string.IsNullOrEmpty(pushSubject) && messaging["EmailFromAddress"]?.GetValue<string>() is { Length: > 0 } address)
        {
            pushSubject = "mailto:" + address;
        }

        if (pushSubject.Length > 0
            && (!pushSubject.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase)
                || !MailAddress.TryCreate(pushSubject[7..], out var contact)
                || contact.Address != pushSubject[7..]))
        {
            throw new InvalidOperationException("Configure a mailto push contact.");
        }

        var statePath = Path.Combine(options.StateDirectory, "identity.json");
        var result = new InstallationResult { ServiceCount = ServiceNames.Length };
        JsonObject state;
        if (File.Exists(statePath))
        {
            state = ReadObject(statePath);
            ValidateState(state, options.ApiDatabaseProvider);
        }
        else
        {
            if (ServiceNames.Any(name => File.Exists(Path.Combine(options.SettingsDirectory, name, "settings.json")))
                || File.Exists(Path.Combine(options.SettingsDirectory, "postgres", "admin-password")))
            {
                throw new InvalidOperationException("Restore the installation-state volume before starting existing databases.");
            }

            state = CreateState(options, result);
            CreateDirectory(options.StateDirectory);
            WritePrivateFile(statePath, state.ToJsonString(JsonOptions));
        }

        LocalHttpsCertificates.Prepare(options);
        var services = state["services"]!.AsObject();
        var publicKeys = new JsonObject();
        var endpoints = new JsonObject();
        foreach (var name in ServiceNames)
        {
            publicKeys[name] = services[name]!["publicKey"]!.GetValue<string>();
            endpoints[name] = $"http://{(name == "identity" ? "api" : name)}:8080";
        }

        var databaseStatements = new List<string>();
        foreach (var name in ServiceNames)
        {
            var service = services[name]!;
            var password = service["databasePassword"]!.GetValue<string>();
            var platform = new JsonObject
            {
                ["Name"] = name,
                ["DatabaseProvider"] = "PostgreSql",
                ["ConnectionString"] = $"Host=postgres;Port=5432;Database=dispute_{name};Username=portal_{name};Password={password};SSL Mode=Disable",
                ["SigningKey"] = service["signingKey"]!.GetValue<string>(),
                ["PublicKeys"] = publicKeys.DeepClone(),
                ["Endpoints"] = endpoints.DeepClone(),
                ["BrokerEnabled"] = false
            };
            var settings = new JsonObject { ["Platform"] = platform };
            if (name is "identity" or "ledger" or "transactions")
            {
                settings["Assessment"] = new JsonObject { ["PopulateAccounts"] = options.PopulateAssessmentAccounts };
            }

            if (name == "identity")
            {
                platform["Enabled"] = true;
                platform["CanonicalDisputes"] = true;
                if (options.ApiDatabaseProvider == "SqlServer")
                {
                    platform["DatabaseProvider"] = "SqlServer";
                    platform["ConnectionString"] = $"Server=sqlserver,1433;Database=DisputePortal;User Id=portal_identity;Password={password};Encrypt=True;TrustServerCertificate=True";
                }

                settings["Database"] = new JsonObject { ["Provider"] = options.ApiDatabaseProvider };
                settings["Assessment"]!["SkipContactVerification"] = !options.RequireContactVerification;
                settings["Authentication"] = new JsonObject
                {
                    ["LoginOtpEnabled"] = false,
                    ["RequireStaffMfa"] = false,
                    ["AllowStaffLoginPreference"] = false
                };
                settings["DevelopmentSeed"] = new JsonObject { ["Enabled"] = true, ["Users"] = state["staff"]!.DeepClone() };
                settings["PasswordSecurity"] = new JsonObject { ["CheckBreaches"] = false };
                var origins = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    origin.GetLeftPart(UriPartial.Authority),
                    "http://localhost:8080",
                    $"https://localhost:{options.HttpsPort}"
                };
                if (options.LocalAddress.Length > 0)
                {
                    origins.Add($"https://{options.LocalAddress}:{options.HttpsPort}");
                }

                settings["AllowedOrigins"] = new JsonArray(origins.Select(value => (JsonNode?)JsonValue.Create(value)).ToArray());
                var delivery = messaging.DeepClone().AsObject();
                delivery["Enabled"] = options.ExternalMessagesEnabled && messaging["Enabled"]?.GetValue<bool>() == true;
                delivery["OtpHashKey"] = state["otpHashKey"]!.GetValue<string>();
                delivery["PortalPublicUrl"] = origin.GetLeftPart(UriPartial.Authority);
                settings["Infobip"] = delivery;
                settings["DataProtection"] = new JsonObject { ["ApplicationName"] = "DisputePortal.LocalAssessment" };
                settings["Push"] = new JsonObject
                {
                    ["Enabled"] = pushSubject.Length > 0,
                    ["Subject"] = pushSubject,
                    ["PublicKey"] = state["push"]!["publicKey"]!.GetValue<string>(),
                    ["PrivateKey"] = state["push"]!["privateKey"]!.GetValue<string>()
                };
            }

            var target = Path.Combine(options.SettingsDirectory, name);
            CreateDirectory(target);
            WritePrivateFile(Path.Combine(target, "settings.json"), settings.ToJsonString(JsonOptions) + "\n");

            if (name != "identity" || options.ApiDatabaseProvider == "PostgreSql")
            {
                databaseStatements.Add($"CREATE ROLE portal_{name} LOGIN PASSWORD '{password}';");
                databaseStatements.Add($"CREATE DATABASE dispute_{name} OWNER portal_{name};");
            }
        }

        WriteDatabaseSetup(options, state, databaseStatements);
        result.Fingerprint = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(publicKeys.ToJsonString())));
        return result;
    }

    private static JsonObject CreateState(InstallationOptions options, InstallationResult result)
    {
        var staff = JsonSerializer.Deserialize<List<InitialAdministrator>>(File.ReadAllText(options.StaffFile))
            ?? throw new InvalidOperationException("Initial administrator settings are required.");
        if (staff.Count != 1 || !staff[0].Roles.SequenceEqual(["super_admin"]))
        {
            throw new InvalidOperationException("Configure exactly one initial administrator.");
        }

        var passwordHasher = new PasswordHasher<InitialAdministrator>();
        var users = new JsonArray();
        foreach (var user in staff)
        {
            var password = "Dp!" + NewSecret();
            user.PasswordHash = passwordHasher.HashPassword(user, password);
            users.Add(JsonSerializer.SerializeToNode(user));
            result.InitialAccess.Add(new InitialAccess { Email = user.Email, Roles = user.Roles, Password = password });
        }

        var services = new JsonObject();
        foreach (var name in ServiceNames)
        {
            using var key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            services[name] = new JsonObject
            {
                ["databasePassword"] = "Dp!" + NewSecret(),
                ["signingKey"] = key.ExportPkcs8PrivateKeyPem(),
                ["publicKey"] = key.ExportSubjectPublicKeyInfoPem()
            };
        }

        using var push = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var parameters = push.ExportParameters(true);
        byte[] publicPoint = [4, .. parameters.Q.X!, .. parameters.Q.Y!];
        return new JsonObject
        {
            ["version"] = 1,
            ["apiDatabaseProvider"] = options.ApiDatabaseProvider,
            ["sqlAdminPassword"] = "Dp!" + NewSecret(),
            ["databaseAdminPassword"] = NewSecret(),
            ["otpHashKey"] = NewSecret(),
            ["services"] = services,
            ["staff"] = users,
            ["push"] = new JsonObject
            {
                ["publicKey"] = Convert.ToBase64String(publicPoint).TrimEnd('=').Replace('+', '-').Replace('/', '_'),
                ["privateKey"] = Convert.ToBase64String(parameters.D!).TrimEnd('=').Replace('+', '-').Replace('/', '_')
            }
        };
    }

    private static void ValidateState(JsonObject state, string provider)
    {
        if (state["version"]?.GetValue<int>() != 1 || state["staff"] is not JsonArray { Count: 1 }
            || state["databaseAdminPassword"]?.GetValue<string>().Length != 64
            || state["otpHashKey"]?.GetValue<string>().Length != 64
            || state["push"]?["publicKey"]?.GetValue<string>().Length != 87
            || state["push"]?["privateKey"]?.GetValue<string>().Length != 43
            || state["sqlAdminPassword"]?.GetValue<string>().Length != 67)
        {
            throw new InvalidOperationException("Restore the incomplete installation identity; credentials were not replaced.");
        }

        if (state["apiDatabaseProvider"]?.GetValue<string>() != provider)
        {
            throw new InvalidOperationException("The existing installation uses different database settings.");
        }

        foreach (var name in ServiceNames)
        {
            var service = state["services"]?[name];
            var password = service?["databasePassword"]?.GetValue<string>() ?? "";
            if (!password.StartsWith("Dp!", StringComparison.Ordinal) || password.Length != 67
                || password.AsSpan(3).ContainsAnyExcept("0123456789abcdef"))
            {
                throw new InvalidOperationException("Restore the invalid installation credentials.");
            }

            using var privateKey = ECDsa.Create();
            using var publicKey = ECDsa.Create();
            privateKey.ImportFromPem(service!["signingKey"]!.GetValue<string>());
            publicKey.ImportFromPem(service["publicKey"]!.GetValue<string>());
            if (!privateKey.ExportSubjectPublicKeyInfo().AsSpan().SequenceEqual(publicKey.ExportSubjectPublicKeyInfo()))
            {
                throw new InvalidOperationException("Restore the mismatched service keys.");
            }
        }
    }

    private static JsonObject ReadMessagingSettings(string file)
    {
        if (string.IsNullOrEmpty(file))
        {
            return new JsonObject();
        }

        if (ReadObject(file)["Infobip"] is not JsonObject settings)
        {
            throw new InvalidOperationException("Private messaging settings must contain an Infobip object.");
        }

        string[] booleans = ["Enabled", "WhatsAppTemplateIncludesAction", "WhatsAppTemplateUsesOtpButton", "WhatsAppTemplateCodeOnly"];
        string[] strings = ["BaseUrl", "SmsApiKey", "SmsSender", "EmailApiKey", "EmailFromAddress", "WhatsAppApiKey", "WhatsAppSender", "WhatsAppTemplateName", "WhatsAppTemplateLanguage", "NotificationWhatsAppTemplateName"];
        foreach (var (key, value) in settings)
        {
            if (value is not JsonValue jsonValue
                || !(booleans.Contains(key) && jsonValue.TryGetValue<bool>(out _)
                    || strings.Contains(key) && jsonValue.TryGetValue<string>(out _)))
            {
                throw new InvalidOperationException("Private messaging settings contain an unsupported field or value.");
            }
        }

        return settings;
    }

    private static void WriteDatabaseSetup(InstallationOptions options, JsonObject state, List<string> statements)
    {
        var postgres = Path.Combine(options.SettingsDirectory, "postgres");
        CreateDirectory(postgres, true);
        CreateDirectory(Path.Combine(postgres, "init"), true);
        WritePrivateFile(Path.Combine(postgres, "admin-password"), state["databaseAdminPassword"]!.GetValue<string>(), true);
        WritePrivateFile(Path.Combine(postgres, "init", "00-databases.sql"), string.Join('\n', statements) + "\n", true);

        var sqlServer = Path.Combine(options.SettingsDirectory, "sqlserver");
        CreateDirectory(sqlServer, true);
        WritePrivateFile(Path.Combine(sqlServer, "admin-password"), state["sqlAdminPassword"]!.GetValue<string>(), true);
        var password = state["services"]!["identity"]!["databasePassword"]!.GetValue<string>();
        var initializeSql = $$"""
            IF DB_ID(N'DisputePortal') IS NULL
                CREATE DATABASE [DisputePortal];
            GO

            IF SUSER_ID(N'portal_identity') IS NULL
                CREATE LOGIN [portal_identity] WITH PASSWORD = '{{password}}', CHECK_POLICY = ON;
            GO

            USE [DisputePortal];
            GO

            IF USER_ID(N'portal_identity') IS NULL
                CREATE USER [portal_identity] FOR LOGIN [portal_identity];

            GRANT SELECT, INSERT, UPDATE, DELETE, EXECUTE, VIEW DEFINITION TO [portal_identity];
            """;
        WritePrivateFile(Path.Combine(sqlServer, "initialize.sql"), initializeSql, true);
    }

    private static JsonObject ReadObject(string path) => JsonNode.Parse(File.ReadAllText(path))?.AsObject()
        ?? throw new InvalidOperationException("Installation settings must contain a JSON object.");

    private static string NewSecret() => Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));

    private static void CreateDirectory(string path, bool databaseSetup = false)
    {
        Directory.CreateDirectory(path);
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, databaseSetup
                ? UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute | UnixFileMode.OtherRead | UnixFileMode.OtherExecute
                : UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }
    }

    private static void WritePrivateFile(string path, string content, bool databaseSetup = false)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".new";
        var fileOptions = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write
        };
        if (!OperatingSystem.IsWindows())
        {
            fileOptions.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }
        using (var stream = new FileStream(temporary, fileOptions))
        using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
        {
            writer.Write(content.Replace("\r\n", "\n", StringComparison.Ordinal));
        }

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(temporary, databaseSetup
                ? UnixFileMode.UserRead | UnixFileMode.GroupRead | UnixFileMode.OtherRead
                : UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        File.Move(temporary, path, true);
    }
}
