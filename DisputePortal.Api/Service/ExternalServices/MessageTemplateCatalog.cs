using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using DisputePortal.Api.Models.Domain;
using DisputePortal.Api.Interfaces.ExternalServices;

namespace DisputePortal.Api.Service.ExternalServices;

public class MessageTemplateCatalog(string directory) : IMessageTemplates
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    public static IMessageTemplates Default { get; } = new MessageTemplateCatalog(Path.Combine(
        AppContext.BaseDirectory,
        "Templates",
        "Messages"));

    public RenderedMessage Render(string id, IReadOnlyDictionary<string, string> variables)
    {
        if (!Regex.IsMatch(id, @"^(verification|workflow|push)/[A-Za-z]+$", RegexOptions.NonBacktracking))
        {
            throw new ArgumentException("Unknown message template.");
        }

        var file = Path.Combine(directory, id + ".json");
        if (!File.Exists(file) && id.StartsWith("push/", StringComparison.Ordinal))
        {
            file = Path.Combine(directory, "push", "Default.json");
        }

        if (new FileInfo(file).Length > 16384)
        {
            throw new InvalidOperationException("Message template exceeds the supported size.");
        }

        var template = JsonSerializer.Deserialize<Template>(File.ReadAllText(file), Json) ?? throw new InvalidOperationException("Message template is invalid.");
        using var settings = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "settings.json")));
        var values = new Dictionary<string, string>(variables, StringComparer.Ordinal)
        {
            ["brand"] = settings.RootElement.GetProperty("brand").GetString() ?? ""
        };
        string Expand(string text) => Regex.Replace(
            text,
            @"\{([A-Za-z][A-Za-z0-9]*)\}",
            match => values.TryGetValue(match.Groups[1].Value, out var value) ? value : throw new InvalidOperationException("Message template has an unknown placeholder."),
            RegexOptions.NonBacktracking,
            TimeSpan.FromMilliseconds(50));
        var subject = Expand(template.Subject);
        var action = Expand(template.Action);
        values["subject"] = subject;
        values["action"] = action;
        return new(subject, action, Expand(template.Text));
    }

    public void ValidateMessageTemplates()
    {
        var values = new Dictionary<string, string>
        {
            ["code"] = "000000",
            ["minutes"] = "5",
            ["portalLink"] = "",
            ["account"] = "Savings ••••5678",
            ["reference"] = "DP-2026-123456",
            ["reason"] = "The original payment was already refunded.",
            ["kind"] = "Refund",
            ["status"] = "Failed",
            ["amount"] = "100.00 ZAR",
            ["source"] = "Cheque ••••1234",
            ["target"] = "Savings ••••5678"
        };
        foreach (var path in Directory.GetFiles(directory, "*.json", SearchOption.AllDirectories).Where(p => Path.GetFileName(p) != "settings.json"))
            Render(Path.GetRelativePath(directory, path).Replace('\\', '/')[..^5], values);
    }

    private class Template
    {
        [JsonRequired]
        public string Subject { get; set; } = string.Empty;

        [JsonRequired]
        public string Action { get; set; } = string.Empty;

        [JsonRequired]
        public string Text { get; set; } = string.Empty;

        public Template()
        {
        }

        public Template(string Subject, string Action, string Text)
        {
            this.Subject = Subject;
            this.Action = Action;
            this.Text = Text;
        }
    }
}
