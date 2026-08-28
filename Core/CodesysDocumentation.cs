namespace CodesysMcp.Desktop.Core;

public static class CodesysDocumentation
{
    public const string ShortInstructions = """
        Codesys MCP Desktop 是本机 CODESYS V3.5/InoProShop 自动化服务器。先调用 get_codesys_status；未就绪时调用 launch_codesys。项目操作使用实际 projectFilePath，对象路径使用正斜杠；修改前先读取代码或结构，修改后运行 compile_project。在线操作前先编译并连接 PLC 或启用仿真。delete_object 不可撤销；rename_symbol 先保持 dryRun=true；write_variable 会强制变量；下载、启停、写变量和批量修改仅在用户明确要求时执行。工具调用必须遵循 tools/list 的参数 Schema，失败时报告原始 CODESYS 输出，不要连续重试破坏性操作。完整流程、安全规则和路径示例读取 codesys://help，或获取 codesys_usage_guide Prompt。
        """;

    public const string FullUsageGuide = """
        # Codesys MCP Desktop 使用手册

        ## 用途

        本服务器用于自动化 CODESYS V3.5 及其 OEM 平台（例如 InoProShop）。它管理带界面的 IDE 实例，并通过持久化文件 IPC 在 CODESYS ScriptEngine 中串行执行操作。支持项目、IEC 61131-3 代码、编译、设备树、库、PLC Runtime、变量与归档操作。

        ## 推荐流程

        1. 调用 `get_codesys_status`。State 不是 Ready 时调用 `launch_codesys`，等待 IDE 和 watcher 就绪。
        2. 使用 `open_project` 打开项目。`projectFilePath` 可为绝对 Windows 路径；相对路径按桌面设置中的工作目录解析。
        3. 修改前使用 `get_all_pou_code`、`search_code`、`find_references` 或项目资源读取当前状态。
        4. 使用专用创建/修改工具，不要用 `eval_python` 代替常规工具。
        5. 修改后运行 `compile_project`；需要完整诊断时调用 `get_compile_messages`。
        6. 在线操作前先编译，再用 `set_simulation_mode` 或 `connect_to_device`。仅在 Runtime 要求认证时调用 `set_credentials`。
        7. 下载、运行与验证顺序通常为 `download_to_device`、`start_stop_application`、`get_application_state`。

        ## 路径规则

        - 项目路径示例：`C:/Projects/MyPLC.project`。
        - 项目对象路径使用正斜杠，例如 `Application/PLC_PRG`、`Application/MyFB/Method1`。
        - PLC 变量不带 `Application.` 前缀，例如 `GVL_Main.xEnable`、`PLC_PRG.nCounter`。
        - 严格使用 `tools/list` 返回的 JSON Schema，不臆造参数或枚举值。
        - ScriptEngine 不是线程安全的；同一时间只提交一个耗时调用。

        ## 安全规则

        - `set_pou_code` 成功后会保存项目；空字符串表示不修改该部分，不表示清空。
        - `delete_object` 不可撤销，调用前确认完整目标路径；服务器拒绝顶层和已知系统节点。
        - `rename_symbol` 默认 `dryRun=true`。先审查预览，只有用户明确同意后才应用。
        - `write_variable` 会强制变量值。除非用户明确要求，不写 PLC、不下载、不启停应用。
        - 大范围或高风险修改前使用 `create_project_archive` 备份。
        - `eval_python` 仅用于 ScriptEngine API 诊断，不执行来源不明的代码。
        - 工具失败时保留并报告原始 CODESYS 输出；原因未确认前不得连续重试破坏性调用。

        ## 资源

        - `codesys://help`：本手册，不需要启动 CODESYS。
        - `codesys://project/status`：ScriptEngine 和当前项目状态。
        - `codesys://project/{projectPath}/structure`：当前项目结构。
        - `codesys://project/{projectPath}/pou/{pouPath}/code`：POU、Method 或 Property 代码。

        ## 运行环境

        服务器运行于本机 Windows，不依赖 Node.js。AI 客户端连接用户已启动的 localhost Streamable HTTP 地址。不要让 AI 客户端以子进程启动管理员 EXE。CODESYS 可执行文件可能是 OEM 名称（例如 `InoProShop.exe`），实际路径与 Profile 由桌面“设置”页配置。
        """;
}
