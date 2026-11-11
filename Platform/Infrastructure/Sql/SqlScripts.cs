using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace DisputePortal.Platform.Infrastructure.Sql; 
public static class SqlScripts
{
    private static readonly IReadOnlyDictionary<string, string> resources = typeof(SqlScripts).Assembly.GetManifestResourceNames().Where(name => name.StartsWith(
        "DatabaseSql/",
        StringComparison.Ordinal)).ToDictionary(name => name.Replace('\\', '/'), name => name, StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, string> scripts = new(StringComparer.Ordinal);
    private static readonly Regex fragmentPattern = new(@"\{\{([A-Za-z][A-Za-z0-9]*)\}\}", RegexOptions.NonBacktracking);

    public static string ReadSqlScript(string name) => scripts.GetOrAdd(name, LoadEmbeddedSqlScript);

    public static string RenderSqlScript(string name, params (string Name, string Value)[] fragments)
    {
        var values = fragments.ToDictionary(part => part.Name, part => part.Value, StringComparer.Ordinal);
        return fragmentPattern.Replace(
            ReadSqlScript(name),
            match => values.TryGetValue(match.Groups[1].Value, out var value) ? value : throw new InvalidOperationException("A required SQL structural fragment is missing."));
    }

    private static string LoadEmbeddedSqlScript(string name)
    {
        var resource = "DatabaseSql/" + name + ".sql";
        using var stream = (resources.TryGetValue(resource, out var actual) ? typeof(SqlScripts).Assembly.GetManifestResourceStream(actual) : null) ?? throw new InvalidOperationException("The release is missing a required SQL script: " + name);
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
