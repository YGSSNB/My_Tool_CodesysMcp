using System.ComponentModel;
using System.IO;
using ModelContextProtocol.Server;

namespace CodesysMcp.Desktop.Core;

[McpServerResourceType]
public sealed class CodesysResources(CodesysLauncher launcher, ScriptManager scripts, ServerSettings settings)
{
    [McpServerResource(UriTemplate = "codesys://help", Name = "codesys-help"),
     Description("Codesys MCP Desktop 完整使用手册、调用流程、路径规则和安全约束。")]
    public string Help() => CodesysDocumentation.FullUsageGuide;

    [McpServerResource(UriTemplate = "codesys://project/status", Name = "project-status"),
     Description("CODESYS 脚本状态和当前打开项目信息。")]
    public async Task<string> ProjectStatus(CancellationToken cancellationToken = default)
    {
        var result = await launcher.ExecuteScriptAsync(scripts.LoadTemplate("check_status"), cancellationToken: cancellationToken);
        return Format(result);
    }

    [McpServerResource(UriTemplate = "codesys://project/{+projectPath}/structure", Name = "project-structure"),
     Description("读取当前 CODESYS 项目树结构。")]
    public async Task<string> ProjectStructure(string projectPath, CancellationToken cancellationToken = default)
    {
        var script = scripts.PrepareScriptWithHelpers(
            "get_project_structure",
            new Dictionary<string, string> { ["PROJECT_FILE_PATH"] = ResolvePath(projectPath) },
            "_text_utils",
            "require_project_open");
        return Format(await launcher.ExecuteScriptAsync(script, cancellationToken: cancellationToken));
    }

    [McpServerResource(UriTemplate = "codesys://project/{+projectPath}/pou/{+pouPath}/code", Name = "pou-code"),
     Description("读取 POU、Method 或 Property 的声明和实现代码。")]
    public async Task<string> PouCode(string projectPath, string pouPath, CancellationToken cancellationToken = default)
    {
        var script = scripts.PrepareScriptWithHelpers(
            "get_pou_code",
            new Dictionary<string, string>
            {
                ["PROJECT_FILE_PATH"] = ResolvePath(projectPath),
                ["POU_FULL_PATH"] = pouPath.Replace('\\', '/').Trim('/')
            },
            "_text_utils",
            "require_project_open",
            "find_object_by_path");
        return Format(await launcher.ExecuteScriptAsync(script, cancellationToken: cancellationToken));
    }

    private string ResolvePath(string path) => Path.GetFullPath(
        Path.IsPathRooted(path) ? path : Path.Combine(settings.WorkspaceDirectory, path));

    private static string Format(IpcResult result) => result.Success
        ? result.Output
        : throw new InvalidOperationException($"{result.Output}\n{result.Error}".Trim());
}
