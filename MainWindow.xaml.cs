using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using CodesysMcp.Desktop.Core;
using Microsoft.Win32;
using Drawing = System.Drawing;
using Forms = System.Windows.Forms;

namespace MCP转编译;

public partial class MainWindow : Wpf.Ui.Controls.UiWindow
{
    private ServerSettings _settings;
    private ScriptManager _scripts;
    private CodesysLauncher _launcher;
    private McpServerHost _serverHost;
    private Forms.NotifyIcon? _trayIcon;
    private bool _isClosing;
    private bool _exitRequested;

    public MainWindow()
    {
        InitializeComponent();
        _settings = SettingsStore.Load();
        _scripts = new ScriptManager();
        _launcher = null!;
        _serverHost = null!;
        CreateServices();
        CreateTrayIcon();
        LoadSettingsIntoView();
        Closing += MainWindow_Closing;
        StateChanged += MainWindow_StateChanged;
        Loaded += MainWindow_Loaded;
        AppendLog("桌面控制台已启动。当前进程不依赖 Node.js。");
    }

    private void CreateServices()
    {
        _launcher = new CodesysLauncher(_settings, _scripts);
        _launcher.StatusChanged += Launcher_StatusChanged;
        _launcher.LogMessage += (_, message) => AppendLog(message);
        _serverHost = new McpServerHost(_settings, _launcher, _scripts);
        _serverHost.LogMessage += (_, message) => AppendLog(message);
    }

    private void LoadSettingsIntoView()
    {
        CodesysPathBox.Text = _settings.CodesysPath;
        ProfileNameBox.Text = _settings.ProfileName;
        WorkspaceBox.Text = _settings.WorkspaceDirectory;
        PortBox.Text = _settings.HttpPort.ToString();
        CommandTimeoutBox.Text = _settings.CommandTimeoutMs.ToString();
        ReadyTimeoutBox.Text = _settings.ReadyTimeoutMs.ToString();
        AutoLaunchCheckBox.IsChecked = _settings.AutoLaunchCodesys;
        KeepAliveCheckBox.IsChecked = _settings.KeepCodesysAlive;
        CloseBehaviorBox.SelectedValue = _settings.CloseBehavior.ToString();
        UpdateEndpointText();
        UpdateAiConfigurationPrompt();
        UpdateStatus();
    }

    private void SaveViewToSettings()
    {
        if (!int.TryParse(PortBox.Text, out var port) || port is < 1 or > 65535)
        {
            throw new InvalidOperationException("HTTP 端口必须是 1 到 65535 的整数。");
        }
        if (!int.TryParse(CommandTimeoutBox.Text, out var commandTimeout) || commandTimeout < 1_000)
        {
            throw new InvalidOperationException("命令超时必须是不小于 1000 的整数毫秒值。");
        }
        if (!int.TryParse(ReadyTimeoutBox.Text, out var readyTimeout) || readyTimeout < 5_000)
        {
            throw new InvalidOperationException("IDE 就绪超时必须是不小于 5000 的整数毫秒值。");
        }

        _settings.CodesysPath = CodesysPathBox.Text.Trim();
        _settings.ProfileName = ProfileNameBox.Text.Trim();
        _settings.WorkspaceDirectory = WorkspaceBox.Text.Trim();
        _settings.HttpPort = port;
        _settings.CommandTimeoutMs = commandTimeout;
        _settings.ReadyTimeoutMs = readyTimeout;
        _settings.AutoLaunchCodesys = AutoLaunchCheckBox.IsChecked == true;
        _settings.KeepCodesysAlive = KeepAliveCheckBox.IsChecked == true;
        _settings.CloseBehavior = Enum.TryParse<WindowCloseBehavior>(CloseBehaviorBox.SelectedValue?.ToString(), out var closeBehavior)
            ? closeBehavior
            : WindowCloseBehavior.MinimizeToTray;
        SettingsStore.Save(_settings);
        UpdateEndpointText();
        UpdateAiConfigurationPrompt();
        UpdateActiveConfiguration();
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        Loaded -= MainWindow_Loaded;
        await DetectInstallationsAsync(!File.Exists(CodesysPathBox.Text.Trim()));
    }

    private void BrowseCodesys_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择 CODESYS 或 InoProShop 可执行文件",
            Filter = "自动化 IDE|CODESYS.exe;InoProShop.exe|可执行文件|*.exe",
            CheckFileExists = true
        };
        if (dialog.ShowDialog(this) == true)
        {
            CodesysPathBox.Text = dialog.FileName;
            ApplyProfileSuggestion(dialog.FileName);
            UpdateActiveConfiguration();
        }
    }

    private async void DetectInstallations_Click(object sender, RoutedEventArgs e) =>
        await DetectInstallationsAsync(true);

    private async Task DetectInstallationsAsync(bool selectFirst)
    {
        DetectButton.IsEnabled = false;
        DetectionStatusText.Text = "正在扫描注册表和安装目录...";
        try
        {
            var installations = await CodesysInstallationDetector.DetectAsync();
            DetectedInstallationsBox.ItemsSource = installations;
            var configured = installations.FirstOrDefault(item =>
                string.Equals(item.ExecutablePath, CodesysPathBox.Text.Trim(), StringComparison.OrdinalIgnoreCase));
            if (configured is not null)
            {
                DetectedInstallationsBox.SelectedItem = configured;
            }
            else if (selectFirst && installations.Count > 0)
            {
                DetectedInstallationsBox.SelectedIndex = 0;
            }

            DetectionStatusText.Text = installations.Count == 0
                ? "未自动发现，可使用“浏览”选择"
                : $"发现 {installations.Count} 个安装";
            AppendLog($"安装检测完成：发现 {installations.Count} 个 CODESYS/InoProShop 可执行文件。");
        }
        catch (Exception exception)
        {
            DetectionStatusText.Text = "检测失败，仍可人工浏览";
            AppendLog($"安装检测失败: {exception.Message}");
        }
        finally
        {
            DetectButton.IsEnabled = true;
        }
    }

    private void DetectedInstallation_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DetectedInstallationsBox.SelectedItem is not CodesysInstallation installation)
        {
            return;
        }

        CodesysPathBox.Text = installation.ExecutablePath;
        ApplyProfileSuggestion(installation.ExecutablePath);
        DetectionStatusText.Text = $"来源：{installation.Source}";
        UpdateActiveConfiguration();
    }

    private void ApplyProfileSuggestion(string executablePath)
    {
        if (Path.GetFileName(executablePath).Equals("InoProShop.exe", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var match = Regex.Match(executablePath, @"3\.5\.(?<sp>\d+)(?:\.\d+)?", RegexOptions.IgnoreCase);
        if (match.Success)
        {
            ProfileNameBox.Text = $"CODESYS V3.5 SP{match.Groups["sp"].Value}";
        }
    }

    private void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SaveViewToSettings();
            FooterText.Text = "设置已保存";
            AppendLog($"设置已保存到 {SettingsStore.FilePath}");
        }
        catch (Exception exception)
        {
            System.Windows.MessageBox.Show(this, exception.Message, "设置无效", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void LaunchCodesys_Click(object sender, RoutedEventArgs e) =>
        await RunUiActionAsync("正在启动 CODESYS...", async () =>
        {
            SaveViewToSettings();
            await _launcher.LaunchAsync();
        });

    private async void DisconnectCodesys_Click(object sender, RoutedEventArgs e) =>
        await RunUiActionAsync("正在断开 CODESYS...", () => _launcher.ShutdownAsync(false));

    private async void CloseCodesys_Click(object sender, RoutedEventArgs e) =>
        await RunUiActionAsync("正在关闭 CODESYS...", () => _launcher.ShutdownAsync(true));

    private async void StartServer_Click(object sender, RoutedEventArgs e) =>
        await RunUiActionAsync("正在启动 MCP 服务...", async () =>
        {
            SaveViewToSettings();
            await _serverHost.StartHttpAsync();
            if (_settings.AutoLaunchCodesys)
            {
                _ = _launcher.LaunchAsync();
            }
            UpdateStatus();
        });

    private void CopyAiPrompt_Click(object sender, RoutedEventArgs e)
    {
        UpdateAiConfigurationPrompt();
        System.Windows.Clipboard.SetText(AiConfigurationPromptBox.Text);
        FooterText.Text = "已复制，可粘贴给 AI";
    }

    private void RefreshAiPrompt_Click(object sender, RoutedEventArgs e)
    {
        UpdateAiConfigurationPrompt();
        FooterText.Text = "AI 配置提示已刷新";
    }

    private async void StopServer_Click(object sender, RoutedEventArgs e) =>
        await RunUiActionAsync("正在停止 MCP 服务...", async () =>
        {
            await _serverHost.StopAsync();
            UpdateStatus();
        });

    private async void TestConnection_Click(object sender, RoutedEventArgs e) =>
        await RunUiActionAsync("正在执行 MCP initialize 和 tools/list...", async () =>
        {
            if (!_serverHost.IsRunning)
            {
                throw new InvalidOperationException("请先启动 MCP HTTP 服务。");
            }

            TestConnectionButton.IsEnabled = false;
            try
            {
                var result = await McpConnectionTester.TestAsync(_serverHost.Endpoint);
                ToolsList.ItemsSource = result.Tools;
                var helpAvailable = result.Resources.Contains("codesys://help", StringComparer.Ordinal);
                var promptAvailable = result.Prompts.Contains("codesys_usage_guide", StringComparer.Ordinal);
                var descriptionsClean = result.ToolsWithDuplicatedInstructions.Count == 0;
                ToolCountText.Text = $"{result.Tools.Count} 工具 · Help {(helpAvailable ? "正常" : "缺失")} · Prompt {(promptAvailable ? "正常" : "缺失")} · 描述{(descriptionsClean ? "无重复" : "有重复")}";
                AppendLog($"MCP 自检成功：{result.ServerName} {result.ServerVersion}\r\n" +
                          $"工具: {result.Tools.Count}，资源: {result.Resources.Count}，Prompts: {result.Prompts.Count}\r\n" +
                          $"codesys://help: {helpAvailable}，codesys_usage_guide: {promptAvailable}\r\n" +
                          $"工具描述重复全局说明: {(descriptionsClean ? "无" : string.Join(", ", result.ToolsWithDuplicatedInstructions))}\r\n" +
                          $"握手 instructions:\r\n{result.Instructions ?? "(未返回)"}");
            }
            finally
            {
                TestConnectionButton.IsEnabled = true;
            }
        });

    private async void RunScript_Click(object sender, RoutedEventArgs e) =>
        await RunUiActionAsync("正在通过 IPC 执行脚本...", async () =>
        {
            RunScriptButton.IsEnabled = false;
            try
            {
                var result = await _launcher.ExecuteScriptAsync(PythonCodeBox.Text, 30_000);
                ScriptResultText.Text = result.Success ? "执行成功" : "执行失败";
                AppendLog($"脚本结果 success={result.Success}\r\n{result.Output}\r\n{result.Error}".TrimEnd());
            }
            finally
            {
                RunScriptButton.IsEnabled = true;
            }
        });

    private void CopyEndpoint_Click(object sender, RoutedEventArgs e)
    {
        System.Windows.Clipboard.SetText(EndpointBox.Text);
        FooterText.Text = "MCP 地址已复制";
    }

    private void RefreshStatus_Click(object sender, RoutedEventArgs e) => UpdateStatus();

    private void ClearLog_Click(object sender, RoutedEventArgs e) => LogBox.Clear();

    private void OverviewNav_Click(object sender, RoutedEventArgs e) => NavigateTo(0);

    private void TestNav_Click(object sender, RoutedEventArgs e) => NavigateTo(1);

    private void SettingsNav_Click(object sender, RoutedEventArgs e) => NavigateTo(2);

    private void NavigateTo(int pageIndex)
    {
        MainPages.SelectedIndex = pageIndex;
        OverviewNavButton.IsChecked = pageIndex == 0;
        TestNavButton.IsChecked = pageIndex == 1;
        SettingsNavButton.IsChecked = pageIndex == 2;
    }

    private void CreateTrayIcon()
    {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("显示主窗口", null, (_, _) => Dispatcher.Invoke(ShowFromTray));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("启动 MCP 服务", null, (_, _) => Dispatcher.Invoke(async () => await StartServerFromTrayAsync()));
        menu.Items.Add("停止 MCP 服务", null, (_, _) => Dispatcher.Invoke(async () => await StopServerFromTrayAsync()));
        menu.Items.Add("启动 / 连接 IDE", null, (_, _) => Dispatcher.Invoke(async () => await LaunchIdeFromTrayAsync()));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("退出程序", null, (_, _) => Dispatcher.Invoke(RequestExit));

        var iconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico");
        var icon = File.Exists(iconPath)
            ? new Drawing.Icon(iconPath)
            : Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!);
        _trayIcon = new Forms.NotifyIcon
        {
            Icon = icon,
            Text = "CODESYS MCP Desktop",
            ContextMenuStrip = menu,
            Visible = true
        };
        _trayIcon.DoubleClick += (_, _) => Dispatcher.Invoke(ShowFromTray);
    }

    private void MainWindow_StateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized)
        {
            HideToTray();
        }
    }

    private void HideToTray()
    {
        Hide();
        if (_trayIcon is not null)
        {
            _trayIcon.BalloonTipTitle = "CODESYS MCP Desktop";
            _trayIcon.BalloonTipText = "程序继续在托盘运行，MCP 服务不会中断。";
            _trayIcon.ShowBalloonTip(2000);
        }
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
        Topmost = true;
        Topmost = false;
    }

    private async Task StartServerFromTrayAsync()
    {
        try
        {
            SaveViewToSettings();
            await _serverHost.StartHttpAsync();
            UpdateStatus();
        }
        catch (Exception exception)
        {
            ShowTrayError(exception.Message);
        }
    }

    private async Task StopServerFromTrayAsync()
    {
        try
        {
            await _serverHost.StopAsync();
            UpdateStatus();
        }
        catch (Exception exception)
        {
            ShowTrayError(exception.Message);
        }
    }

    private async Task LaunchIdeFromTrayAsync()
    {
        try
        {
            SaveViewToSettings();
            await _launcher.LaunchAsync();
        }
        catch (Exception exception)
        {
            ShowTrayError(exception.Message);
        }
    }

    private void ShowTrayError(string message)
    {
        AppendLog($"托盘操作失败: {message}");
        if (_trayIcon is not null)
        {
            _trayIcon.BalloonTipTitle = "操作失败";
            _trayIcon.BalloonTipText = message.Length > 180 ? message[..180] : message;
            _trayIcon.ShowBalloonTip(4000);
        }
    }

    private void RequestExit()
    {
        _exitRequested = true;
        Show();
        Close();
    }

    private async Task RunUiActionAsync(string status, Func<Task> action)
    {
        FooterText.Text = status;
        try
        {
            await action();
            FooterText.Text = "操作完成";
        }
        catch (Exception exception)
        {
            FooterText.Text = "操作失败";
            AppendLog($"错误: {exception.Message}");
            System.Windows.MessageBox.Show(this, exception.Message, "操作失败", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            UpdateStatus();
        }
    }

    private void Launcher_StatusChanged(object? sender, LauncherStatus e) =>
        Dispatcher.Invoke(UpdateStatus);

    private void UpdateStatus()
    {
        var status = _launcher.Status;
        CodesysStatusText.Text = status.State switch
        {
            CodesysState.Ready => $"已就绪 · PID {status.ProcessId}",
            CodesysState.Launching => "启动中",
            CodesysState.Stopping => "停止中",
            CodesysState.Error => "错误",
            _ => "已停止"
        };
        CodesysStatusText.Foreground = status.State == CodesysState.Ready
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(84, 214, 157))
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 203, 105));

        McpStatusText.Text = _serverHost.IsRunning ? $"运行中 · {_settings.HttpPort}" : "已停止";
        McpStatusText.Foreground = _serverHost.IsRunning
            ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(84, 214, 157))
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(255, 203, 105));
        StartServerButton.IsEnabled = !_serverHost.IsRunning;
        StopServerButton.IsEnabled = _serverHost.IsRunning;
        SessionDetailsText.Text = status.State == CodesysState.Ready
            ? $"PID: {status.ProcessId}\n会话: {status.SessionId}\nIPC: {status.IpcDirectory}"
            : status.LastError is not null ? $"最后错误: {status.LastError}" : "尚未建立 CODESYS 会话";
        UpdateActiveConfiguration();
    }

    private void UpdateEndpointText() => EndpointBox.Text = $"http://127.0.0.1:{PortBox.Text.Trim()}/mcp";

    private void UpdateActiveConfiguration()
    {
        ActiveIdePathText.Text = string.IsNullOrWhiteSpace(CodesysPathBox.Text) ? "尚未配置" : CodesysPathBox.Text.Trim();
        ActiveProfileText.Text = string.IsNullOrWhiteSpace(ProfileNameBox.Text) ? "尚未配置" : ProfileNameBox.Text.Trim();
    }

    private void UpdateAiConfigurationPrompt()
    {
        var port = int.TryParse(PortBox.Text, out var parsedPort) && parsedPort is >= 1 and <= 65535
            ? parsedPort
            : _settings.HttpPort;
        AiConfigurationPromptBox.Text = $"""
            请帮我把下面这个 MCP 服务器添加到你当前使用的客户端配置中：

            名称：codesys
            类型：Streamable HTTP MCP 服务器
            地址：http://127.0.0.1:{port}/mcp

            要求：
            1. 请先识别你当前客户端实际使用的 MCP 配置文件和配置格式，再进行修改；不要猜测文件位置。
            2. 这是一个由用户手动启动、已在本机运行的 HTTP MCP 服务。不要把它配置成 stdio，也不要以子进程启动 CodesysMcp.Desktop.exe。
            3. 保留配置文件中已有的其他 MCP 服务器，不要覆盖无关配置。
            4. 修改前先备份原配置文件；修改后告诉我改了哪个文件、写入了什么配置，以及是否需要重启客户端。
            5. 如果当前客户端不支持 Streamable HTTP MCP，请不要擅自改成其他传输方式，直接说明限制。

            完成配置后，请连接该服务器并执行 MCP initialize 和 tools/list，确认服务器名称为 Codesys MCP Desktop、握手 instructions 已收到，并能看到约 41 个工具。
            """;
    }

    private void AppendLog(string message)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => AppendLog(message));
            return;
        }

        LogBox.AppendText(message + Environment.NewLine);
        LogBox.ScrollToEnd();
    }

    private async void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_isClosing)
        {
            return;
        }

        if (!_exitRequested && _settings.CloseBehavior == WindowCloseBehavior.MinimizeToTray)
        {
            e.Cancel = true;
            HideToTray();
            return;
        }

        e.Cancel = true;
        _isClosing = true;
        Hide();
        try
        {
            SaveViewToSettings();
            using var shutdownTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                await _serverHost.StopAsync(shutdownTimeout.Token);
                if (_launcher.State is not CodesysState.Stopped)
                {
                    await _launcher.ShutdownAsync(!_settings.KeepCodesysAlive, shutdownTimeout.Token);
                }
            }
            catch (OperationCanceledException)
            {
                AppendLog("退出清理超过 5 秒，应用将直接关闭。");
            }
        }
        catch (Exception exception)
        {
            AppendLog($"退出清理失败: {exception.Message}");
        }
        finally
        {
            if (_trayIcon is not null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
                _trayIcon = null;
            }
            Closing -= MainWindow_Closing;
            System.Windows.Application.Current.Shutdown();
            Environment.Exit(0);
        }
    }
}