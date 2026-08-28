namespace CodesysMcp.Desktop.Core;

public enum CodesysState
{
    Stopped,
    Launching,
    Ready,
    Stopping,
    Error
}

public enum WindowCloseBehavior
{
    Exit,
    MinimizeToTray
}

public sealed class ServerSettings
{
    public string CodesysPath { get; set; } = @"C:\Program Files\CODESYS 3.5.20.0\CODESYS\Common\CODESYS.exe";

    public string ProfileName { get; set; } = "CODESYS V3.5 SP20";

    public string WorkspaceDirectory { get; set; } = Environment.CurrentDirectory;

    public int HttpPort { get; set; } = 5180;

    public int CommandTimeoutMs { get; set; } = 60_000;

    public int ReadyTimeoutMs { get; set; } = 180_000;

    public bool AutoLaunchCodesys { get; set; }

    public bool KeepCodesysAlive { get; set; } = true;

    public WindowCloseBehavior CloseBehavior { get; set; } = WindowCloseBehavior.MinimizeToTray;
}

public sealed record LauncherStatus(
    CodesysState State,
    int? ProcessId,
    string? SessionId,
    string? IpcDirectory,
    DateTimeOffset? StartedAt,
    string? LastError);

public sealed record IpcCommand(string RequestId, string ScriptPath, long Timestamp);

public sealed record IpcResult(
    string RequestId,
    bool Success,
    string Output,
    string Error,
    double Timestamp);
