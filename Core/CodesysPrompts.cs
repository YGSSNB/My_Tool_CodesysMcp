using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace CodesysMcp.Desktop.Core;

[McpServerPromptType]
public sealed class CodesysPrompts
{
    [McpServerPrompt(Name = "codesys_usage_guide"),
     Description("获取 CODESYS/InoProShop MCP 的完整操作流程、路径规范和安全规则。")]
    public string UsageGuide() => CodesysDocumentation.FullUsageGuide;
}
