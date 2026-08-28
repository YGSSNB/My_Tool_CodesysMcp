using System.IO;
using System.Reflection;

namespace CodesysMcp.Desktop.Core;

public sealed class ScriptManager
{
    private readonly string _scriptsDirectory;

    public ScriptManager(string? scriptsDirectory = null)
    {
        _scriptsDirectory = scriptsDirectory ?? Path.Combine(AppContext.BaseDirectory, "Scripts");
    }

    public string LoadTemplate(string name)
    {
        var fileName = name.EndsWith(".py", StringComparison.OrdinalIgnoreCase) ? name : $"{name}.py";
        var path = Path.Combine(_scriptsDirectory, fileName);
        if (File.Exists(path))
        {
            return File.ReadAllText(path);
        }

        var resourceName = $"CodesysScripts.{fileName}";
        using var stream = typeof(ScriptManager).Assembly.GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            var available = string.Join(", ", typeof(ScriptManager).Assembly
                .GetManifestResourceNames()
                .Where(resource => resource.StartsWith("CodesysScripts.", StringComparison.Ordinal)));
            throw new FileNotFoundException(
                $"找不到 CODESYS 脚本模板 '{fileName}'。外部路径: {path}；嵌入资源: {resourceName}；可用资源: {available}",
                path);
        }

        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public string PrepareScript(string name, IReadOnlyDictionary<string, string> parameters) =>
        Interpolate(LoadTemplate(name), parameters);

    public string PrepareScriptWithHelpers(
        string name,
        IReadOnlyDictionary<string, string> parameters,
        params string[] helpers)
    {
        var sections = helpers.Select(LoadTemplate).Append(LoadTemplate(name));
        return Interpolate(string.Join("\n\n", sections), parameters);
    }

    public static string Interpolate(string template, IReadOnlyDictionary<string, string> parameters)
    {
        var result = template;
        foreach (var (key, value) in parameters)
        {
            result = result.Replace($"{{{key}:raw}}", value, StringComparison.Ordinal);
            result = result.Replace($"{{{key}}}", EscapePythonString(value), StringComparison.Ordinal);
        }

        return result;
    }

    private static string EscapePythonString(string value) => value
        .Replace("\\", "\\\\", StringComparison.Ordinal)
        .Replace("\"", "\\\"", StringComparison.Ordinal)
        .Replace("'", "\\'", StringComparison.Ordinal)
        .Replace("\n", "\\n", StringComparison.Ordinal)
        .Replace("\r", "\\r", StringComparison.Ordinal)
        .Replace("\t", "\\t", StringComparison.Ordinal);
}
