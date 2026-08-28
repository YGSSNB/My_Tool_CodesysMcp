using System.Diagnostics;
using System.IO;
using System.Security;
using Microsoft.Win32;

namespace CodesysMcp.Desktop.Core;

public sealed record CodesysInstallation(string DisplayName, string ExecutablePath, string Source)
{
    public override string ToString() => $"{DisplayName}  ·  {ExecutablePath}";
}

public static class CodesysInstallationDetector
{
    private static readonly string[] ExecutableNames = ["CODESYS.exe", "InoProShop.exe"];

    public static Task<IReadOnlyList<CodesysInstallation>> DetectAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => Detect(cancellationToken), cancellationToken);

    private static IReadOnlyList<CodesysInstallation> Detect(CancellationToken cancellationToken)
    {
        var results = new Dictionary<string, CodesysInstallation>(StringComparer.OrdinalIgnoreCase);
        ScanRunningProcesses(results);
        AddKnownDefault(results);
        ScanAppPaths(results);
        ScanUninstallRegistry(results);
        ScanCommonDirectories(results, cancellationToken);

        return results.Values
            .Where(item => File.Exists(item.ExecutablePath))
            .OrderBy(item => item.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(item => item.ExecutablePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void ScanRunningProcesses(IDictionary<string, CodesysInstallation> results)
    {
        foreach (var processName in new[] { "CODESYS", "InoProShop" })
        {
            foreach (var process in Process.GetProcessesByName(processName))
            {
                using (process)
                {
                    try
                    {
                        if (process.MainModule?.FileName is string path)
                        {
                            Add(results, path, Path.GetFileNameWithoutExtension(path), "当前运行进程");
                        }
                    }
                    catch (InvalidOperationException)
                    {
                    }
                    catch (System.ComponentModel.Win32Exception)
                    {
                    }
                }
            }
        }
    }

    private static void AddKnownDefault(IDictionary<string, CodesysInstallation> results)
    {
        foreach (var baseDirectory in ProgramDirectories())
        {
            for (var servicePack = 11; servicePack <= 21; servicePack++)
            {
                Add(results,
                    Path.Combine(baseDirectory, $"CODESYS 3.5.{servicePack}.0", "CODESYS", "Common", "CODESYS.exe"),
                    $"CODESYS V3.5 SP{servicePack}",
                    "常见安装路径");
            }
        }
    }

    private static void ScanAppPaths(IDictionary<string, CodesysInstallation> results)
    {
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            foreach (var hive in new[] { RegistryHive.LocalMachine, RegistryHive.CurrentUser })
            {
                try
                {
                    using var root = RegistryKey.OpenBaseKey(hive, view);
                    foreach (var executableName in ExecutableNames)
                    {
                        using var key = root.OpenSubKey($@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\{executableName}");
                        if (key?.GetValue(null) is string path)
                        {
                            Add(results, NormalizeExecutablePath(path), Path.GetFileNameWithoutExtension(executableName), "App Paths 注册表");
                        }
                    }
                }
                catch (SecurityException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }
    }

    private static void ScanUninstallRegistry(IDictionary<string, CodesysInstallation> results)
    {
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var uninstall = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                if (uninstall is null)
                {
                    continue;
                }

                foreach (var subKeyName in uninstall.GetSubKeyNames())
                {
                    using var entry = uninstall.OpenSubKey(subKeyName);
                    if (entry is null)
                    {
                        continue;
                    }

                    var displayName = entry.GetValue("DisplayName") as string;
                    if (string.IsNullOrWhiteSpace(displayName) ||
                        (!displayName.Contains("CODESYS", StringComparison.OrdinalIgnoreCase) &&
                         !displayName.Contains("InoProShop", StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    var displayIcon = entry.GetValue("DisplayIcon") as string;
                    if (!string.IsNullOrWhiteSpace(displayIcon))
                    {
                        Add(results, NormalizeExecutablePath(displayIcon), displayName, "卸载注册表");
                    }

                    var installLocation = entry.GetValue("InstallLocation") as string;
                    if (!string.IsNullOrWhiteSpace(installLocation))
                    {
                        foreach (var executableName in ExecutableNames)
                        {
                            AddFirstMatch(results, installLocation, executableName, displayName, "卸载注册表");
                        }
                    }
                }
            }
            catch (SecurityException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static void ScanCommonDirectories(
        IDictionary<string, CodesysInstallation> results,
        CancellationToken cancellationToken)
    {
        foreach (var root in ProgramDirectories().Where(Directory.Exists))
        {
            var pending = new Queue<(string Directory, int Depth)>();
            pending.Enqueue((root, 0));
            while (pending.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var (directory, depth) = pending.Dequeue();
                try
                {
                    foreach (var file in Directory.EnumerateFiles(directory, "*.exe", SearchOption.TopDirectoryOnly))
                    {
                        var name = Path.GetFileName(file);
                        if (ExecutableNames.Contains(name, StringComparer.OrdinalIgnoreCase))
                        {
                            Add(results, file, Path.GetFileNameWithoutExtension(file), "安装目录扫描");
                        }
                    }

                    if (depth >= 5)
                    {
                        continue;
                    }

                    foreach (var child in Directory.EnumerateDirectories(directory))
                    {
                        var folderName = Path.GetFileName(child);
                        if (depth == 0 &&
                            !folderName.Contains("CODESYS", StringComparison.OrdinalIgnoreCase) &&
                            !folderName.Contains("InoPro", StringComparison.OrdinalIgnoreCase) &&
                            !folderName.Contains("ifm", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        pending.Enqueue((child, depth + 1));
                    }
                }
                catch (UnauthorizedAccessException)
                {
                }
                catch (IOException)
                {
                }
            }
        }
    }

    private static void AddFirstMatch(
        IDictionary<string, CodesysInstallation> results,
        string directory,
        string executableName,
        string displayName,
        string source)
    {
        try
        {
            var match = Directory.EnumerateFiles(directory, executableName, SearchOption.AllDirectories).FirstOrDefault();
            if (match is not null)
            {
                Add(results, match, displayName, source);
            }
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (IOException)
        {
        }
    }

    private static void Add(
        IDictionary<string, CodesysInstallation> results,
        string path,
        string displayName,
        string source)
    {
        path = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
        if (File.Exists(path))
        {
            results[path] = new CodesysInstallation(displayName, Path.GetFullPath(path), source);
        }
    }

    private static string NormalizeExecutablePath(string value)
    {
        var firstPart = value.Split(',')[0].Trim().Trim('"');
        return firstPart.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? firstPart : string.Empty;
    }

    private static IEnumerable<string> ProgramDirectories()
    {
        yield return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        yield return Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
    }
}
