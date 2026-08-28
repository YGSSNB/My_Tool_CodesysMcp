using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace CodesysMcp.Desktop.Core;

public sealed class McpServerHost(ServerSettings settings, CodesysLauncher launcher, ScriptManager scripts) : IAsyncDisposable
{
    private WebApplication? _application;

    public event EventHandler<string>? LogMessage;

    public bool IsRunning => _application is not null;

    public Uri Endpoint => new($"http://127.0.0.1:{settings.HttpPort}/mcp");

    public async Task StartHttpAsync(CancellationToken cancellationToken = default)
    {
        if (_application is not null)
        {
            return;
        }

        var options = new WebApplicationOptions
        {
            ApplicationName = typeof(McpServerHost).Assembly.FullName,
            Args = [],
            ContentRootPath = AppContext.BaseDirectory
        };
        var builder = WebApplication.CreateBuilder(options);
        builder.WebHost.UseUrls($"http://127.0.0.1:{settings.HttpPort}");
        builder.Logging.ClearProviders();
        builder.Services.AddSingleton(settings);
        builder.Services.AddSingleton(launcher);
        builder.Services.AddSingleton(scripts);
        builder.Services
            .AddMcpServer(server =>
            {
                server.ServerInfo = new() { Name = "Codesys MCP Desktop", Version = "1.0.0" };
                server.ServerInstructions = CodesysDocumentation.ShortInstructions;
            })
            .WithHttpTransport()
            .WithTools<CodesysTools>()
            .WithResources<CodesysResources>()
            .WithPrompts<CodesysPrompts>();

        var app = builder.Build();
        app.MapGet("/", () => Results.Json(new
        {
            name = "Codesys MCP Desktop",
            transport = "Streamable HTTP",
            endpoint = Endpoint.ToString(),
            tools = 41,
            resources = 4,
            prompts = 1
        }));
        app.MapMcp("/mcp");

        await app.StartAsync(cancellationToken);
        _application = app;
        WriteLog($"MCP HTTP 服务已启动: {Endpoint}");
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_application is null)
        {
            return;
        }

        await _application.StopAsync(cancellationToken);
        await _application.DisposeAsync();
        _application = null;
        WriteLog("MCP HTTP 服务已停止。");
    }

    private void WriteLog(string message) =>
        LogMessage?.Invoke(this, $"[{DateTime.Now:HH:mm:ss}] {message}");

    public async ValueTask DisposeAsync() => await StopAsync();
}
