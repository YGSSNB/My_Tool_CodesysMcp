# CODESYS MCP Desktop

Windows `.NET 10` 桌面 MCP 服务器，用于通过 AI 客户端自动化 CODESYS V3.5 和 InoProShop 等 OEM 平台。

## 运行

1. 以管理员权限启动 `CodesysMcp.Desktop.exe`。
2. 在“设置”页自动检测或浏览选择 `CODESYS.exe` / `InoProShop.exe`，确认 Profile。
3. 在“运行”页启动 MCP 服务，默认地址为 `http://127.0.0.1:5180/mcp`。
4. 在“设置”页复制配置提示并粘贴给 AI，让 AI 按当前客户端格式添加该 HTTP MCP。
5. 在“测试”页执行 `initialize + tools/list` 验证连接。

AI 握手只携带精简全局规则。完整使用手册通过 `codesys://help` 资源或 `codesys_usage_guide` Prompt 获取，避免在 41 个工具描述中重复消耗上下文。

程序支持最小化到托盘。托盘右键可显示窗口、启动/停止 MCP、启动/连接 IDE 或退出程序。点击窗口关闭按钮的行为可在“设置”页选择。

桌面界面使用 WPF-UI Fluent 主题和左侧导航。设置表单采用高 DPI 自适应网格布局。

## 上游

Node.js 参考实现和 IronPython 脚本来自：

https://github.com/luke-harriman/Codesys-MCP

当前导入快照位于 `Codesys-MCP-main/`，版本为 `0.6.3`。详细架构映射、兼容契约和上游升级流程见 [docs/PROJECT-UPSTREAM.md](docs/PROJECT-UPSTREAM.md)。

## 发布

项目配置为 Windows x64 自包含单文件发布。发布输出：

```text
publish/CodesysMcp.Desktop.exe
```

Python 脚本已嵌入 EXE；发布目录中的 `Scripts/` 可作为外部覆盖版本。
