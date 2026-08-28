using ModelContextProtocol.Client;

namespace CodesysMcp.Desktop.Core;

public static class McpConnectionTester
{
    public sealed record ConnectionTestResult(
        string ServerName,
        string ServerVersion,
        string? Instructions,
        IReadOnlyList<string> Tools,
        IReadOnlyList<string> Resources,
        IReadOnlyList<string> Prompts,
        IReadOnlyList<string> ToolsWithDuplicatedInstructions);

    public static async Task<ConnectionTestResult> TestAsync(
        Uri endpoint,
        CancellationToken cancellationToken = default)
    {
        var transport = new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint = endpoint,
            Name = "Codesys MCP Desktop Self Test"
        });
        await using var client = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken);
        var tools = await client.ListToolsAsync(cancellationToken: cancellationToken);
        var resources = await client.ListResourcesAsync(cancellationToken: cancellationToken);
        var prompts = await client.ListPromptsAsync(cancellationToken: cancellationToken);
        var serverInfo = client.ServerInfo;
        const string duplicatedPrefix = "Codesys MCP Desktop 是本机";
        return new ConnectionTestResult(
            serverInfo.Name,
            serverInfo.Version,
            client.ServerInstructions,
            tools.Select(tool => tool.Name).Order(StringComparer.Ordinal).ToArray(),
            resources.Select(resource => resource.Uri).Order(StringComparer.Ordinal).ToArray(),
            prompts.Select(prompt => prompt.Name).Order(StringComparer.Ordinal).ToArray(),
            tools.Where(tool => tool.Description?.Contains(duplicatedPrefix, StringComparison.Ordinal) == true)
                .Select(tool => tool.Name)
                .Order(StringComparer.Ordinal)
                .ToArray());
    }
}
