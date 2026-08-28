using System.Diagnostics;
using System.IO;

namespace CodesysMcp.Desktop.Core;

public sealed class CodesysLauncher(ServerSettings settings, ScriptManager scripts) : IAsyncDisposable
{
    private const string SessionDirectoryName = "codesys-mcp-persistent";
    private Process? _process;
    private IpcClient? _ipcClient;
    private string? _sessionId;
    private string? _ipcDirectory;
    private DateTimeOffset? _startedAt;
    private string? _lastError;

    public event EventHandler<LauncherStatus>? StatusChanged;

    public event EventHandler<string>? LogMessage;

    public CodesysState State { get; private set; } = CodesysState.Stopped;

    public LauncherStatus Status => new(
        State,
        GetProcessId(),
        _sessionId,
        _ipcDirectory,
        _startedAt,
        _lastError);

    public async Task LaunchAsync(CancellationToken cancellationToken = default)
    {
        if (State is CodesysState.Ready or CodesysState.Launching)
        {
            return;
        }

        if (!File.Exists(settings.CodesysPath))
        {
            Fail($"找不到 CODESYS.exe: {settings.CodesysPath}");
            throw new FileNotFoundException(_lastError, settings.CodesysPath);
        }

        SetState(CodesysState.Launching);
        try
        {
            if (TryAdoptExistingSession())
            {
                WriteLog($"已连接现有 CODESYS 会话，PID {GetProcessId()}。");
                return;
            }

            _sessionId = Guid.NewGuid().ToString();
            _ipcDirectory = Path.Combine(Path.GetTempPath(), SessionDirectoryName, _sessionId);
            _ipcClient = new IpcClient(_ipcDirectory, settings.CommandTimeoutMs);
            _ipcClient.EnsureDirectories();

            var watcher = ScriptManager.Interpolate(
                scripts.LoadTemplate("watcher"),
                new Dictionary<string, string> { ["IPC_BASE_DIR"] = _ipcDirectory });
            var watcherPath = Path.Combine(_ipcDirectory, "watcher.py");
            await File.WriteAllTextAsync(watcherPath, watcher, cancellationToken);

            var startInfo = new ProcessStartInfo
            {
                FileName = settings.CodesysPath,
                WorkingDirectory = Path.GetDirectoryName(settings.CodesysPath)!,
                UseShellExecute = false,
                CreateNoWindow = false
            };
            startInfo.ArgumentList.Add($"--profile={settings.ProfileName}");
            startInfo.ArgumentList.Add($"--runscript={watcherPath}");

            _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            _process.Exited += OnProcessExited;
            if (!_process.Start())
            {
                throw new InvalidOperationException("CODESYS 进程未能启动。");
            }

            WriteLog($"CODESYS 已启动，PID {_process.Id}，等待 watcher 就绪。");
            using var readyTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            readyTimeout.CancelAfter(settings.ReadyTimeoutMs);
            while (!_ipcClient.IsReady)
            {
                if (_process.HasExited)
                {
                    throw new InvalidOperationException($"CODESYS 启动期间退出，代码 {_process.ExitCode}。");
                }

                await Task.Delay(500, readyTimeout.Token);
            }

            _startedAt = DateTimeOffset.Now;
            _lastError = null;
            SetState(CodesysState.Ready);
            WriteLog($"CODESYS watcher 已就绪: {_ipcDirectory}");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Fail($"等待 CODESYS watcher 超时（{settings.ReadyTimeoutMs / 1000} 秒）。");
            throw new TimeoutException(_lastError);
        }
        catch (Exception exception)
        {
            Fail(exception.Message);
            throw;
        }
    }

    public Task<IpcResult> ExecuteScriptAsync(
        string script,
        int? timeoutMs = null,
        CancellationToken cancellationToken = default)
    {
        if (State != CodesysState.Ready || _ipcClient is null)
        {
            throw new InvalidOperationException($"CODESYS 尚未就绪，当前状态: {State}。");
        }

        return _ipcClient.SendCommandAsync(script, timeoutMs, cancellationToken);
    }

    public async Task ShutdownAsync(bool closeCodesys, CancellationToken cancellationToken = default)
    {
        if (State is CodesysState.Stopped or CodesysState.Stopping)
        {
            return;
        }

        SetState(CodesysState.Stopping);
        if (!closeCodesys)
        {
            await DisposeIpcAsync();
            _process?.Dispose();
            _process = null;
            _startedAt = null;
            SetState(CodesysState.Stopped);
            WriteLog("已断开 CODESYS，CODESYS 和 watcher 继续运行。");
            return;
        }

        if (_ipcClient is not null)
        {
            await _ipcClient.SendTerminateAsync(cancellationToken);
        }

        if (closeCodesys && _process is { HasExited: false })
        {
            if (_process.CloseMainWindow())
            {
                await _process.WaitForExitAsync(cancellationToken).WaitAsync(TimeSpan.FromSeconds(8), cancellationToken);
            }
            else
            {
                _process.Kill(true);
                await _process.WaitForExitAsync(cancellationToken);
            }
        }

        await DisposeIpcAsync();
        _process?.Dispose();
        _process = null;
        _startedAt = null;
        SetState(CodesysState.Stopped);
        WriteLog("CODESYS 会话已关闭。");
    }

    private bool TryAdoptExistingSession()
    {
        var root = Path.Combine(Path.GetTempPath(), SessionDirectoryName);
        if (!Directory.Exists(root))
        {
            return false;
        }

        foreach (var readyFile in Directory.EnumerateFiles(root, "ready.signal", SearchOption.AllDirectories)
                     .OrderByDescending(File.GetLastWriteTimeUtc))
        {
            try
            {
                using var json = System.Text.Json.JsonDocument.Parse(File.ReadAllText(readyFile));
                if (!json.RootElement.TryGetProperty("pid", out var pidElement))
                {
                    continue;
                }

                var process = Process.GetProcessById(pidElement.GetInt32());
                if (process.HasExited)
                {
                    continue;
                }

                _process = process;
                _sessionId = Path.GetFileName(Path.GetDirectoryName(readyFile));
                _ipcDirectory = Path.GetDirectoryName(readyFile);
                _ipcClient = new IpcClient(_ipcDirectory!, settings.CommandTimeoutMs);
                _ipcClient.EnsureDirectories();
                _startedAt = File.GetLastWriteTime(readyFile);
                _lastError = null;
                SetState(CodesysState.Ready);
                return true;
            }
            catch (ArgumentException)
            {
            }
            catch (InvalidOperationException)
            {
            }
            catch (System.Text.Json.JsonException)
            {
            }
        }

        return false;
    }

    private int? GetProcessId()
    {
        try
        {
            return _process is { HasExited: false } ? _process.Id : null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    private async void OnProcessExited(object? sender, EventArgs e)
    {
        if (State != CodesysState.Stopping)
        {
            Fail("CODESYS 进程已退出。");
        }

        await DisposeIpcAsync();
    }

    private async Task DisposeIpcAsync()
    {
        if (_ipcClient is not null)
        {
            await _ipcClient.DisposeAsync();
            _ipcClient = null;
        }
    }

    private void Fail(string message)
    {
        _lastError = message;
        SetState(CodesysState.Error);
        WriteLog($"错误: {message}");
    }

    private void SetState(CodesysState state)
    {
        State = state;
        StatusChanged?.Invoke(this, Status);
    }

    private void WriteLog(string message) =>
        LogMessage?.Invoke(this, $"[{DateTime.Now:HH:mm:ss}] {message}");

    public async ValueTask DisposeAsync()
    {
        if (State is not CodesysState.Stopped)
        {
            await ShutdownAsync(!settings.KeepCodesysAlive);
        }

        _process?.Dispose();
    }
}
