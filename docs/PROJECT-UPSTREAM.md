# CODESYS MCP Desktop 项目记录

## 项目目标

本项目将 Node.js MCP 服务器迁移为 Windows `.NET 10` WPF 桌面程序，提供独立 EXE、管理员权限、CODESYS/InoProShop 启动管理、MCP Streamable HTTP 服务、协议测试、托盘运行和配置界面。

桌面程序不需要 Node.js。CODESYS ScriptEngine 侧继续使用上游 IronPython 脚本及文件 IPC 契约，以保持工具行为兼容。

## 上游来源

- 上游仓库：https://github.com/luke-harriman/Codesys-MCP
- 本地上游快照目录：`Codesys-MCP-main/`
- 导入时包名：`codesys-mcp-persistent`
- 导入时版本：`0.6.3`（来自 `Codesys-MCP-main/package.json`）
- 上游许可证：MIT（见 `Codesys-MCP-main/LICENSE`）
- 导入提交：未记录。当前环境没有可用的 `git` 命令，后续同步时应补录提交哈希或 release tag。

`Codesys-MCP-main` 应视为上游参考快照。桌面程序的 C# 实现位于 `Core/`，不应通过覆盖上游目录的方式修改 C# 迁移层。

## 架构映射

| 上游 Node 模块 | .NET 迁移模块 | 说明 |
|---|---|---|
| `src/bin.ts` | `App.xaml.cs`、`Core/McpServerHost.cs` | GUI / stdio 入口与 MCP 宿主 |
| `src/server.ts` | `Core/CodesysTools.cs`、`Core/CodesysResources.cs`、`Core/CodesysPrompts.cs` | 41 个工具、4 个资源、1 个 Prompt、握手说明 |
| `src/launcher.ts` | `Core/CodesysLauncher.cs` | IDE 进程和持久会话管理 |
| `src/ipc.ts` | `Core/IpcClient.cs` | 原子文件写入、命令串行化和结果轮询 |
| `src/script-manager.ts` | `Core/ScriptManager.cs` | Python 模板、helper 合并和参数转义 |
| `src/scripts/*.py` | 原样嵌入 EXE，并可由输出目录 `Scripts/` 覆盖 | CODESYS ScriptEngine 实际操作 |
| CLI 参数 | `Core/Models.cs`、`Core/SettingsStore.cs`、设置页 | 持久化桌面配置 |

## 必须保持的兼容契约

1. MCP 工具名保持上游 `snake_case` 名称，当前基线为 41 个。
2. MCP 资源在上游 3 个 `codesys://` URI 基础上增加 `codesys://help` 完整手册；提供 `codesys_usage_guide` Prompt。
3. IPC 目录保持 `%TEMP%/codesys-mcp-persistent/<sessionId>/`。
4. 命令顺序保持先写 `<id>.py`，再原子写 `<id>.command.json`。
5. 结果文件保持 `<id>.result.json` 及 `requestId/success/output/error/timestamp` 字段。
6. CODESYS 操作必须串行执行。
7. Python 参数必须按 IronPython 字符串规则转义。
8. `SCRIPT_SUCCESS` 和 `SCRIPT_ERROR` 标记语义保持兼容。
9. Python 模板既嵌入单文件 EXE，也复制到 `Scripts/` 目录；外部脚本优先，便于现场修补。

## MCP 上下文与 token 约束

1. `initialize.instructions` 仅放约 300–600 字的服务器定位、推荐入口、关键路径规则和高风险操作约束。
2. 每个工具描述仅说明该工具用途、必要前置条件和该工具特有风险，不得拼接全局说明或完整手册。
3. 完整操作手册只维护在 `Core/CodesysDocumentation.cs` 的 `FullUsageGuide`，通过 `codesys://help` 和 `codesys_usage_guide` 提供。
4. 协议自检必须检查工具描述是否意外包含全局说明前缀，防止 41 份重复上下文。

## 上游升级流程

1. 记录当前上游版本、提交或 release tag，并备份 `Codesys-MCP-main/`。
2. 从上游仓库获取新版本到独立临时目录，不要先覆盖当前目录。
3. 比较 `package.json`、`README.md`、`ARCHITECTURE.md`、`src/server.ts`、`src/ipc.ts`、`src/launcher.ts` 和全部 `src/scripts/*.py`。
4. 优先同步 Python 模板变化，并检查模板占位符、helper 依赖和超时变化。
5. 对比 `server.ts`：新增、删除或改名的工具必须同步到 `CodesysTools.cs`；资源变化同步到 `CodesysResources.cs`。
6. 对比 IPC 和 watcher 协议。若文件格式或生命周期改变，先更新 `IpcClient.cs` / `CodesysLauncher.cs`，再替换脚本。
7. 不要把 Node SDK、commander 或 zod 引入桌面运行时；JSON Schema 继续由官方 .NET MCP SDK 生成。
8. 更新本文件中的版本、提交、工具数、资源数和差异说明。
9. 执行验证清单，通过后才发布新 EXE。

## 升级验证清单

- `dotnet build` 无错误、无警告。
- 应用以管理员权限启动，图标和托盘图标正常。
- 自动检测 CODESYS/InoProShop，人工浏览仍可用。
- 点击关闭按钮按设置执行退出或最小化到托盘。
- 托盘双击恢复窗口；右键启动/停止 MCP、启动 IDE、退出均有效。
- MCP `initialize` 返回服务器名称、版本和 `instructions`。
- `tools/list` 数量和名称与上游一致。
- `codesys://help` 和原有项目资源可以列出/读取。
- `codesys_usage_guide` Prompt 可以列出/获取。
- 工具描述未重复包含 `initialize.instructions`。
- 在无外部 `Scripts/` 目录时仍可加载嵌入模板。
- CODESYS/InoProShop watcher 写入 `ready.signal`。
- 至少验证状态、打开项目、读取结构、修改代码、编译。
- 有测试 PLC 时再验证连接、下载、启停和变量读写。
- 发布为 `win-x64` 自包含单文件，并在干净目录启动验证。

## 已知环境差异

- InoProShop 是基于 CODESYS 的 OEM 产品，可执行文件名和 Profile 名称不一定符合标准 CODESYS 命名。自动检测只确认程序路径，Profile 必须允许人工校正。
- 桌面 EXE 使用 `requireAdministrator`，用于启动需要管理员权限的 InoProShop。
- MCP 客户端应连接桌面程序已启动的 `http://127.0.0.1:<port>/mcp`，不应以子进程启动管理员 EXE。
- 上游文档指出不同 CODESYS SP 版本的持久 watcher API 可能有差异。升级或切换 OEM 版本时必须实际测试 ScriptEngine。

## 桌面 UI 约束

- UI 使用 `WPF-UI 2.0.3` 的 `UiWindow`、Fluent 主题和 `SymbolIcon`，主框架为左侧导航加单页面工作区。
- 页面包括概览、测试和设置；运行日志固定在概览页底部。
- 设置表单必须使用 `Grid.RowDefinitions` 或顺序布局。禁止使用固定大幅 `Margin` 将控件叠放到输入框下方。
- 必须在 Windows Per-Monitor V2 DPI 模式下保持文本、输入框、复选项和下拉框不重叠。
- 托盘和业务控件名称由 `MainWindow.xaml.cs` 使用；视觉重构时不得随意删除现有 `x:Name` 或事件处理器。
