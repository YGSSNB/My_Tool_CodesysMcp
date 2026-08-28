using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using ModelContextProtocol.Server;

namespace CodesysMcp.Desktop.Core;

[McpServerToolType]
public sealed class CodesysTools(CodesysLauncher launcher, ScriptManager scripts, ServerSettings settings)
{
    private static readonly string[] ProjectHelpers = ["_text_utils", "ensure_project_open"];
    private static readonly string[] ObjectHelpers = ["_text_utils", "ensure_project_open", "find_object_by_path"];
    private static readonly string[] OnlineHelpers = ["_text_utils", "ensure_project_open", "ensure_online_connection"];

    [McpServerTool(Name = "launch_codesys"), Description("启动带界面的 CODESYS 持久实例。")]
    public async Task<string> LaunchCodesys(CancellationToken cancellationToken = default)
    {
        await launcher.LaunchAsync(cancellationToken);
        return FormatStatus();
    }

    [McpServerTool(Name = "shutdown_codesys"), Description("关闭由服务器管理的 CODESYS 持久实例。")]
    public async Task<string> ShutdownCodesys(CancellationToken cancellationToken = default)
    {
        await launcher.ShutdownAsync(true, cancellationToken);
        return "CODESYS 已关闭。";
    }

    [McpServerTool(Name = "get_codesys_status"), Description("获取 CODESYS 会话状态、PID 和 IPC 目录。")]
    public string GetCodesysStatus() => FormatStatus();

    [McpServerTool(Name = "eval_python"), Description("在当前 CODESYS ScriptEngine 中执行 IronPython 2.7。仅用于诊断。")]
    public Task<string> EvalPython(
        [Description("IronPython 源代码；应输出 SCRIPT_SUCCESS。")]
        string code,
        [Description("执行超时毫秒数。")]
        int timeoutMs = 30_000,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync(code, timeoutMs, cancellationToken);

    [McpServerTool(Name = "open_project"), Description("打开已有的 CODESYS .project 项目。")]
    public Task<string> OpenProject(string filePath, CancellationToken cancellationToken = default) =>
        RunAsync("open_project", P(("PROJECT_FILE_PATH", ResolvePath(filePath))), ProjectHelpers, null, cancellationToken);

    [McpServerTool(Name = "create_project"), Description("从模板创建 CODESYS 项目。templateName 优先于 templatePath。")]
    public Task<string> CreateProject(
        string filePath,
        string? templatePath = null,
        string? templateName = null,
        CancellationToken cancellationToken = default)
    {
        var mode = !string.IsNullOrWhiteSpace(templateName) ? "name" : "path";
        var resolvedTemplate = mode == "path" ? ResolveTemplatePath(templatePath) : string.Empty;
        return RunAsync("create_project", P(
            ("TEMPLATE_MODE", mode),
            ("PROJECT_FILE_PATH", ResolvePath(filePath)),
            ("TEMPLATE_PROJECT_PATH", resolvedTemplate),
            ("TEMPLATE_NAME", templateName?.Trim() ?? string.Empty)), [], null, cancellationToken);
    }

    [McpServerTool(Name = "list_project_templates"), Description("列出 CODESYS 已注册和文件系统中的项目模板。")]
    public Task<string> ListProjectTemplates(string? extraTemplateDir = null, CancellationToken cancellationToken = default) =>
        RunAsync("list_project_templates", P(("EXTRA_TEMPLATE_DIR", extraTemplateDir ?? string.Empty)), ["_text_utils"], 60_000, cancellationToken);

    [McpServerTool(Name = "save_project"), Description("保存当前 CODESYS 项目。")]
    public Task<string> SaveProject(string projectFilePath, CancellationToken cancellationToken = default) =>
        RunAsync("save_project", P(("PROJECT_FILE_PATH", ResolvePath(projectFilePath))), ProjectHelpers, null, cancellationToken);

    [McpServerTool(Name = "create_pou"), Description("创建 Program、FunctionBlock 或 Function POU。")]
    public Task<string> CreatePou(
        string projectFilePath,
        string name,
        string type,
        string language,
        string parentPath,
        CancellationToken cancellationToken = default) =>
        RunAsync("create_pou", P(
            ("PROJECT_FILE_PATH", ResolvePath(projectFilePath)),
            ("POU_NAME", name.Trim()),
            ("POU_TYPE_STR", type),
            ("IMPL_LANGUAGE_STR", language),
            ("PARENT_PATH", SanitizePath(parentPath))), ObjectHelpers, null, cancellationToken);

    [McpServerTool(Name = "set_pou_code"), Description("设置 POU、Method 或 Property 的声明和实现代码。空值表示保持不变。")]
    public Task<string> SetPouCode(
        string projectFilePath,
        string pouPath,
        string? declarationCode = null,
        string? implementationCode = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(declarationCode) && string.IsNullOrEmpty(implementationCode))
        {
            throw new ArgumentException("declarationCode 和 implementationCode 至少提供一项非空内容。");
        }

        return RunAsync("set_pou_code", P(
            ("PROJECT_FILE_PATH", ResolvePath(projectFilePath)),
            ("POU_FULL_PATH", SanitizePath(pouPath)),
            ("DECLARATION_CONTENT", declarationCode ?? string.Empty),
            ("IMPLEMENTATION_CONTENT", implementationCode ?? string.Empty),
            ("UPDATE_DECL", string.IsNullOrEmpty(declarationCode) ? "0" : "1"),
            ("UPDATE_IMPL", string.IsNullOrEmpty(implementationCode) ? "0" : "1")), ObjectHelpers, null, cancellationToken);
    }

    [McpServerTool(Name = "create_property"), Description("在 Function Block 下创建 Property。")]
    public Task<string> CreateProperty(
        string projectFilePath,
        string parentPouPath,
        string propertyName,
        string propertyType,
        CancellationToken cancellationToken = default) =>
        RunAsync("create_property", P(
            ("PROJECT_FILE_PATH", ResolvePath(projectFilePath)),
            ("PARENT_POU_FULL_PATH", SanitizePath(parentPouPath)),
            ("PROPERTY_NAME", propertyName.Trim()),
            ("PROPERTY_TYPE", propertyType.Trim())), ObjectHelpers, null, cancellationToken);

    [McpServerTool(Name = "create_method"), Description("在 Function Block 下创建 Method。")]
    public Task<string> CreateMethod(
        string projectFilePath,
        string parentPouPath,
        string methodName,
        string? returnType = null,
        CancellationToken cancellationToken = default) =>
        RunAsync("create_method", P(
            ("PROJECT_FILE_PATH", ResolvePath(projectFilePath)),
            ("PARENT_POU_FULL_PATH", SanitizePath(parentPouPath)),
            ("METHOD_NAME", methodName.Trim()),
            ("RETURN_TYPE", returnType?.Trim() ?? string.Empty)), ObjectHelpers, null, cancellationToken);

    [McpServerTool(Name = "compile_project"), Description("编译 CODESYS 项目并返回编译输出。")]
    public Task<string> CompileProject(string projectFilePath, CancellationToken cancellationToken = default) =>
        RunAsync("compile_project", P(("PROJECT_FILE_PATH", ResolvePath(projectFilePath))), ProjectHelpers, 120_000, cancellationToken);

    [McpServerTool(Name = "get_compile_messages"), Description("读取上一次编译产生的消息。")]
    public Task<string> GetCompileMessages(string projectFilePath, CancellationToken cancellationToken = default) =>
        RunAsync("get_compile_messages", P(("PROJECT_FILE_PATH", ResolvePath(projectFilePath))), ProjectHelpers, null, cancellationToken);

    [McpServerTool(Name = "create_dut"), Description("创建 Structure、Enumeration、Union 或 Alias DUT。")]
    public Task<string> CreateDut(
        string projectFilePath,
        string name,
        string dutType,
        string parentPath,
        CancellationToken cancellationToken = default) =>
        RunAsync("create_dut", P(
            ("PROJECT_FILE_PATH", ResolvePath(projectFilePath)),
            ("DUT_NAME", name.Trim()),
            ("DUT_TYPE_STR", dutType),
            ("PARENT_PATH", SanitizePath(parentPath))), ObjectHelpers, null, cancellationToken);

    [McpServerTool(Name = "create_gvl"), Description("创建全局变量列表 GVL。")]
    public Task<string> CreateGvl(
        string projectFilePath,
        string name,
        string parentPath,
        string? declarationCode = null,
        CancellationToken cancellationToken = default) =>
        RunAsync("create_gvl", P(
            ("PROJECT_FILE_PATH", ResolvePath(projectFilePath)),
            ("GVL_NAME", name.Trim()),
            ("PARENT_PATH", SanitizePath(parentPath)),
            ("DECLARATION_CONTENT", declarationCode ?? string.Empty)), ObjectHelpers, null, cancellationToken);

    [McpServerTool(Name = "create_folder"), Description("在项目树中创建组织文件夹。")]
    public Task<string> CreateFolder(
        string projectFilePath,
        string folderName,
        string parentPath,
        CancellationToken cancellationToken = default) =>
        RunAsync("create_folder", P(
            ("PROJECT_FILE_PATH", ResolvePath(projectFilePath)),
            ("FOLDER_NAME", folderName.Trim()),
            ("PARENT_PATH", SanitizePath(parentPath))), ObjectHelpers, null, cancellationToken);

    [McpServerTool(Name = "delete_object"), Description("删除用户创建的项目对象。此操作不可撤销。")]
    public Task<string> DeleteObject(string projectFilePath, string objectPath, CancellationToken cancellationToken = default)
    {
        var path = SanitizePath(objectPath);
        if (!path.Contains('/'))
        {
            throw new ArgumentException("拒绝删除顶层或系统对象。");
        }

        return RunAsync("delete_object", P(
            ("PROJECT_FILE_PATH", ResolvePath(projectFilePath)),
            ("OBJECT_PATH", path)), ObjectHelpers, null, cancellationToken);
    }

    [McpServerTool(Name = "rename_object"), Description("重命名项目对象。")]
    public Task<string> RenameObject(
        string projectFilePath,
        string objectPath,
        string newName,
        CancellationToken cancellationToken = default) =>
        RunAsync("rename_object", P(
            ("PROJECT_FILE_PATH", ResolvePath(projectFilePath)),
            ("OBJECT_PATH", SanitizePath(objectPath)),
            ("NEW_NAME", newName.Trim())), ObjectHelpers, null, cancellationToken);

    [McpServerTool(Name = "get_all_pou_code"), Description("批量读取项目内全部 POU、DUT 和 GVL 代码。")]
    public Task<string> GetAllPouCode(string projectFilePath, CancellationToken cancellationToken = default) =>
        RunAsync("get_all_pou_code", P(("PROJECT_FILE_PATH", ResolvePath(projectFilePath))), ProjectHelpers, 120_000, cancellationToken);

    [McpServerTool(Name = "search_code"), Description("在项目文本代码中进行正则或字面量搜索。")]
    public Task<string> SearchCode(
        string projectFilePath,
        string pattern,
        bool regex = true,
        bool caseSensitive = true,
        bool includeDeclaration = true,
        bool includeImplementation = true,
        int maxHits = 1000,
        CancellationToken cancellationToken = default) =>
        RunAsync("search_code", P(
            ("PROJECT_FILE_PATH", ResolvePath(projectFilePath)),
            ("PATTERN", pattern),
            ("USE_REGEX", Flag(regex)),
            ("CASE_SENSITIVE", Flag(caseSensitive)),
            ("INCLUDE_DECL", Flag(includeDeclaration)),
            ("INCLUDE_IMPL", Flag(includeImplementation)),
            ("MAX_HITS", maxHits.ToString())), ProjectHelpers, 120_000, cancellationToken);

    [McpServerTool(Name = "connect_to_device"), Description("连接 PLC Runtime，可选设置 PLC IP 和网关名称。")]
    public Task<string> ConnectToDevice(
        string projectFilePath,
        string? ipAddress = null,
        string? gatewayName = null,
        CancellationToken cancellationToken = default) =>
        RunAsync("connect_to_device", P(
            ("PROJECT_FILE_PATH", ResolvePath(projectFilePath)),
            ("IP_ADDRESS", ipAddress?.Trim() ?? string.Empty),
            ("GATEWAY_NAME", gatewayName?.Trim() ?? string.Empty)), ["ensure_project_open", "ensure_online_connection"], 60_000, cancellationToken);

    [McpServerTool(Name = "set_credentials"), Description("设置本会话后续 PLC 登录使用的用户名和密码。")]
    public Task<string> SetCredentials(string username, string password, CancellationToken cancellationToken = default) =>
        RunAsync("set_credentials", P(("USERNAME", username), ("PASSWORD", password)), [], 10_000, cancellationToken);

    [McpServerTool(Name = "set_simulation_mode"), Description("启用或禁用 PLC 仿真模式。")]
    public Task<string> SetSimulationMode(
        string projectFilePath,
        bool enable,
        CancellationToken cancellationToken = default) =>
        RunAsync("set_simulation_mode", P(
            ("PROJECT_FILE_PATH", ResolvePath(projectFilePath)),
            ("ENABLE", enable ? "true" : "false")), ProjectHelpers, 30_000, cancellationToken);

    [McpServerTool(Name = "disconnect_from_device"), Description("从 PLC Runtime 注销。未连接时也视为成功。")]
    public Task<string> DisconnectFromDevice(string projectFilePath, CancellationToken cancellationToken = default) =>
        RunAsync("disconnect_from_device", P(("PROJECT_FILE_PATH", ResolvePath(projectFilePath))), ["ensure_project_open"], null, cancellationToken);

    [McpServerTool(Name = "get_application_state"), Description("读取 PLC 应用运行、停止、异常和登录状态。")]
    public Task<string> GetApplicationState(string projectFilePath, CancellationToken cancellationToken = default) =>
        RunAsync("get_application_state", P(("PROJECT_FILE_PATH", ResolvePath(projectFilePath))), ["ensure_project_open", "ensure_online_connection"], null, cancellationToken);

    [McpServerTool(Name = "read_variable"), Description("读取运行中 PLC 的变量值。")]
    public Task<string> ReadVariable(
        string projectFilePath,
        string variablePath,
        CancellationToken cancellationToken = default) =>
        RunAsync("read_variable", P(
            ("PROJECT_FILE_PATH", ResolvePath(projectFilePath)),
            ("VARIABLE_PATH", variablePath.Trim())), OnlineHelpers, null, cancellationToken);

    [McpServerTool(Name = "write_variable"), Description("写入并强制运行中 PLC 的变量值。")]
    public Task<string> WriteVariable(
        string projectFilePath,
        string variablePath,
        string value,
        CancellationToken cancellationToken = default) =>
        RunAsync("write_variable", P(
            ("PROJECT_FILE_PATH", ResolvePath(projectFilePath)),
            ("VARIABLE_PATH", variablePath.Trim()),
            ("VARIABLE_VALUE", value)), ["ensure_project_open", "ensure_online_connection"], null, cancellationToken);

    [McpServerTool(Name = "download_to_device"), Description("将编译后的应用下载到 PLC。mode: auto、online_change 或 full。")]
    public Task<string> DownloadToDevice(
        string projectFilePath,
        string mode = "auto",
        CancellationToken cancellationToken = default) =>
        RunAsync("download_to_device", P(
            ("PROJECT_FILE_PATH", ResolvePath(projectFilePath)),
            ("MODE", mode)), ["ensure_project_open", "ensure_online_connection"], 120_000, cancellationToken);

    [McpServerTool(Name = "start_stop_application"), Description("启动或停止已连接的 PLC 应用。action: start 或 stop。")]
    public Task<string> StartStopApplication(
        string projectFilePath,
        string action,
        CancellationToken cancellationToken = default) =>
        RunAsync("start_stop_application", P(
            ("PROJECT_FILE_PATH", ResolvePath(projectFilePath)),
            ("APP_ACTION", action)), ["ensure_project_open", "ensure_online_connection"], null, cancellationToken);

    [McpServerTool(Name = "list_project_libraries"), Description("列出项目引用的 CODESYS 库。")]
    public Task<string> ListProjectLibraries(string projectFilePath, CancellationToken cancellationToken = default) =>
        RunAsync("list_project_libraries", P(("PROJECT_FILE_PATH", ResolvePath(projectFilePath))), ProjectHelpers, null, cancellationToken);

    [McpServerTool(Name = "list_device_repository"), Description("枚举本机 CODESYS Device Repository 中的设备描述。")]
    public Task<string> ListDeviceRepository(
        string? vendor = null,
        string? nameContains = null,
        int maxResults = 500,
        CancellationToken cancellationToken = default) =>
        RunAsync("list_device_repository", P(
            ("VENDOR_FILTER", vendor ?? string.Empty),
            ("NAME_FILTER", nameContains ?? string.Empty),
            ("MAX_RESULTS", maxResults.ToString())), ["_text_utils"], 60_000, cancellationToken);

    [McpServerTool(Name = "map_io_channel"), Description("将 fieldbus I/O 通道绑定到全局变量，或清除绑定。")]
    public Task<string> MapIoChannel(
        string projectFilePath,
        string devicePath,
        string channelPath,
        string? variableName = null,
        bool clearBinding = false,
        CancellationToken cancellationToken = default) =>
        RunAsync("map_io_channel", P(
            ("PROJECT_FILE_PATH", ResolvePath(projectFilePath)),
            ("DEVICE_PATH", SanitizePath(devicePath)),
            ("CHANNEL_PATH", channelPath),
            ("VARIABLE_NAME", variableName ?? string.Empty),
            ("CLEAR_BINDING", Flag(clearBinding))), ObjectHelpers, 30_000, cancellationToken);

    [McpServerTool(Name = "inspect_device_node"), Description("读取设备节点描述、参数、当前值和子设备。")]
    public Task<string> InspectDeviceNode(
        string projectFilePath,
        string devicePath,
        CancellationToken cancellationToken = default) =>
        RunAsync("inspect_device_node", P(
            ("PROJECT_FILE_PATH", ResolvePath(projectFilePath)),
            ("DEVICE_PATH", SanitizePath(devicePath))), ["_text_utils", "require_project_open", "find_object_by_path"], 30_000, cancellationToken);

    [McpServerTool(Name = "add_device"), Description("在项目设备树指定父节点下添加设备。")]
    public Task<string> AddDevice(
        string projectFilePath,
        string parentDevicePath,
        string deviceName,
        int deviceType,
        int? deviceId = null,
        string? version = null,
        CancellationToken cancellationToken = default) =>
        RunAsync("add_device", P(
            ("PROJECT_FILE_PATH", ResolvePath(projectFilePath)),
            ("PARENT_DEVICE_PATH", SanitizePath(parentDevicePath)),
            ("DEVICE_NAME", deviceName.Trim()),
            ("DEVICE_TYPE", deviceType.ToString()),
            ("DEVICE_ID", deviceId?.ToString() ?? string.Empty),
            ("DEVICE_VERSION", version ?? string.Empty)), ObjectHelpers, 60_000, cancellationToken);

    [McpServerTool(Name = "set_device_parameter"), Description("实验性：设置设备参数。先用 inspect_device_node 查询参数 ID。")]
    public Task<string> SetDeviceParameter(
        string projectFilePath,
        string devicePath,
        string parameterId,
        string value,
        CancellationToken cancellationToken = default) =>
        RunAsync("set_device_parameter", P(
            ("PROJECT_FILE_PATH", ResolvePath(projectFilePath)),
            ("DEVICE_PATH", SanitizePath(devicePath)),
            ("PARAMETER_ID", parameterId),
            ("VALUE", value)), ObjectHelpers, 30_000, cancellationToken);

    [McpServerTool(Name = "find_references"), Description("查找符号在全部文本代码中的单词边界引用。")]
    public Task<string> FindReferences(
        string projectFilePath,
        string symbol,
        bool caseSensitive = true,
        int maxHits = 1000,
        CancellationToken cancellationToken = default)
    {
        var pattern = $@"\b{Regex.Escape(symbol)}\b";
        return SearchCode(projectFilePath, pattern, true, caseSensitive, true, true, maxHits, cancellationToken);
    }

    [McpServerTool(Name = "rename_symbol"), Description("跨文本 POU 执行符号重命名。默认 dryRun=true。")]
    public Task<string> RenameSymbol(
        string projectFilePath,
        string oldName,
        string newName,
        bool dryRun = true,
        bool includeDeclaration = true,
        bool includeImplementation = true,
        CancellationToken cancellationToken = default) =>
        RunAsync("rename_symbol", P(
            ("PROJECT_FILE_PATH", ResolvePath(projectFilePath)),
            ("OLD_NAME", oldName),
            ("NEW_NAME", newName),
            ("DRY_RUN", Flag(dryRun)),
            ("INCLUDE_DECL", Flag(includeDeclaration)),
            ("INCLUDE_IMPL", Flag(includeImplementation))), ProjectHelpers, 120_000, cancellationToken);

    [McpServerTool(Name = "monitor_variables"), Description("按固定间隔采样一个或多个 PLC 变量，最长 60 秒。")]
    public Task<string> MonitorVariables(
        string projectFilePath,
        string[] variablePaths,
        int durationMs,
        int intervalMs,
        CancellationToken cancellationToken = default)
    {
        var duration = Math.Min(durationMs, 60_000);
        return RunAsync("monitor_variables", P(
            ("PROJECT_FILE_PATH", ResolvePath(projectFilePath)),
            ("VARIABLES_JSON", JsonSerializer.Serialize(variablePaths)),
            ("DURATION_MS", duration.ToString()),
            ("INTERVAL_MS", Math.Max(intervalMs, 10).ToString())), OnlineHelpers, duration + 30_000, cancellationToken);
    }

    [McpServerTool(Name = "create_project_archive"), Description("将当前已打开项目保存为 .projectarchive。")]
    public Task<string> CreateProjectArchive(
        string projectFilePath,
        string outputPath,
        string? comment = null,
        bool includeLibraries = true,
        bool includeCompiledLibraries = true,
        CancellationToken cancellationToken = default) =>
        RunAsync("create_project_archive", P(
            ("PROJECT_FILE_PATH", ResolvePath(projectFilePath)),
            ("ARCHIVE_PATH", ResolvePath(outputPath)),
            ("COMMENT", comment ?? string.Empty),
            ("INCLUDE_LIBRARIES", Flag(includeLibraries)),
            ("INCLUDE_COMPILED", Flag(includeCompiledLibraries))), ["_text_utils", "require_project_open"], 120_000, cancellationToken);

    [McpServerTool(Name = "add_library"), Description("向项目添加已安装的 CODESYS 库引用。")]
    public Task<string> AddLibrary(
        string projectFilePath,
        string libraryName,
        CancellationToken cancellationToken = default) =>
        RunAsync("add_library", P(
            ("PROJECT_FILE_PATH", ResolvePath(projectFilePath)),
            ("LIBRARY_NAME", libraryName.Trim())), ProjectHelpers, null, cancellationToken);

    private async Task<string> RunAsync(
        string template,
        IReadOnlyDictionary<string, string> parameters,
        string[] helpers,
        int? timeoutMs,
        CancellationToken cancellationToken)
    {
        var script = helpers.Length == 0
            ? scripts.PrepareScript(template, parameters)
            : scripts.PrepareScriptWithHelpers(template, parameters, helpers);
        return await ExecuteAsync(script, timeoutMs, cancellationToken);
    }

    private async Task<string> ExecuteAsync(string script, int? timeoutMs, CancellationToken cancellationToken)
    {
        var result = await launcher.ExecuteScriptAsync(script, timeoutMs, cancellationToken);
        if (!result.Success || result.Output.Contains("SCRIPT_ERROR", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"CODESYS 脚本执行失败。\n{result.Output}\n{result.Error}".Trim());
        }

        return string.IsNullOrWhiteSpace(result.Output) ? "操作成功。" : result.Output.Trim();
    }

    private string ResolvePath(string path) => Path.GetFullPath(
        Path.IsPathRooted(path) ? path : Path.Combine(settings.WorkspaceDirectory, path));

    private string ResolveTemplatePath(string? templatePath)
    {
        if (!string.IsNullOrWhiteSpace(templatePath))
        {
            var resolved = ResolvePath(templatePath);
            return File.Exists(resolved) ? resolved : throw new FileNotFoundException("项目模板不存在。", resolved);
        }

        var root = Directory.GetParent(Path.GetDirectoryName(settings.CodesysPath)!)?.Parent?.FullName;
        var standard = root is null ? string.Empty : Path.Combine(root, "Templates", "Standard.project");
        return File.Exists(standard)
            ? standard
            : throw new FileNotFoundException("未找到 Standard.project，请显式提供 templatePath 或 templateName。");
    }

    private string FormatStatus()
    {
        var status = launcher.Status;
        return $"State: {status.State}\nPID: {status.ProcessId?.ToString() ?? "N/A"}\nSession: {status.SessionId ?? "N/A"}\nIPC: {status.IpcDirectory ?? "N/A"}\nError: {status.LastError ?? "None"}";
    }

    private static Dictionary<string, string> P(params (string Key, string Value)[] values) =>
        values.ToDictionary(static value => value.Key, static value => value.Value, StringComparer.Ordinal);

    private static string SanitizePath(string path) => path.Replace('\\', '/').Trim('/');

    private static string Flag(bool value) => value ? "1" : "0";
}
