using System.IO;
using System.Text;
using System.Text.Json;

namespace CodesysMcp.Desktop.Core;

public sealed class IpcClient(string baseDirectory, int commandTimeoutMs = 60_000) : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly SemaphoreSlim _commandLock = new(1, 1);
    private readonly string _commandsDirectory = Path.Combine(baseDirectory, "commands");
    private readonly string _resultsDirectory = Path.Combine(baseDirectory, "results");

    public string BaseDirectory { get; } = baseDirectory;

    public void EnsureDirectories()
    {
        Directory.CreateDirectory(_commandsDirectory);
        Directory.CreateDirectory(_resultsDirectory);
    }

    public bool IsReady => File.Exists(Path.Combine(BaseDirectory, "ready.signal"));

    public Task SendTerminateAsync(CancellationToken cancellationToken = default) =>
        AtomicWriteAsync(
            Path.Combine(BaseDirectory, "terminate.signal"),
            JsonSerializer.Serialize(new { timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }),
            cancellationToken);

    public async Task<IpcResult> SendCommandAsync(
        string scriptContent,
        int? timeoutMs = null,
        CancellationToken cancellationToken = default)
    {
        await _commandLock.WaitAsync(cancellationToken);
        try
        {
            EnsureDirectories();
            var requestId = Guid.NewGuid().ToString();
            var scriptPath = Path.Combine(_commandsDirectory, $"{requestId}.py");
            var commandPath = Path.Combine(_commandsDirectory, $"{requestId}.command.json");
            var resultPath = Path.Combine(_resultsDirectory, $"{requestId}.result.json");

            await AtomicWriteAsync(scriptPath, scriptContent, cancellationToken);
            var command = new IpcCommand(requestId, scriptPath, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
            await AtomicWriteAsync(commandPath, JsonSerializer.Serialize(command, JsonOptions), cancellationToken);

            var timeout = TimeSpan.FromMilliseconds(timeoutMs ?? commandTimeoutMs);
            var started = DateTime.UtcNow;
            var delay = 100;
            while (DateTime.UtcNow - started < timeout)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (File.Exists(resultPath))
                {
                    var result = await ReadResultAsync(resultPath, requestId, cancellationToken);
                    if (result is not null)
                    {
                        TryDelete(resultPath);
                        return result;
                    }
                }

                await Task.Delay(delay, cancellationToken);
                delay = Math.Min(delay * 2, 1_000);
            }

            TryDelete(scriptPath);
            TryDelete(commandPath);
            throw new TimeoutException($"命令 {requestId} 等待结果超时（{timeout.TotalSeconds:0} 秒）。");
        }
        finally
        {
            _commandLock.Release();
        }
    }

    private static async Task<IpcResult?> ReadResultAsync(
        string resultPath,
        string requestId,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                var json = await File.ReadAllTextAsync(resultPath, cancellationToken);
                var result = JsonSerializer.Deserialize<IpcResult>(json, JsonOptions);
                return result?.RequestId == requestId ? result : null;
            }
            catch (IOException) when (attempt < 2)
            {
                await Task.Delay(100, cancellationToken);
            }
            catch (JsonException) when (attempt < 2)
            {
                await Task.Delay(100, cancellationToken);
            }
        }

        return null;
    }

    private static async Task AtomicWriteAsync(string path, string content, CancellationToken cancellationToken)
    {
        var temporaryPath = $"{path}.tmp";
        await using (var stream = new FileStream(
            temporaryPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            4096,
            FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            var bytes = Encoding.UTF8.GetBytes(content);
            await stream.WriteAsync(bytes, cancellationToken);
            await stream.FlushAsync(cancellationToken);
        }

        File.Move(temporaryPath, path, true);
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    public ValueTask DisposeAsync()
    {
        _commandLock.Dispose();
        return ValueTask.CompletedTask;
    }
}
