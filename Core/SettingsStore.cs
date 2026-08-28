using System.IO;
using System.Text.Json;

namespace CodesysMcp.Desktop.Core;

public static class SettingsStore
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CodesysMcpDesktop",
        "settings.json");

    public static ServerSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                return JsonSerializer.Deserialize<ServerSettings>(File.ReadAllText(FilePath), Options)
                    ?? new ServerSettings();
            }
        }
        catch (JsonException)
        {
        }
        catch (IOException)
        {
        }

        return new ServerSettings();
    }

    public static void Save(ServerSettings settings)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, Options));
    }
}
