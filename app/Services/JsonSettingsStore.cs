using System.IO;
using System.Text.Json;

namespace CpuTempWidget.Services;

/// <summary>%APPDATA%\CpuTempWidget\settings.json (same shape as before the refactor).</summary>
public sealed class JsonSettingsStore : ISettingsStore
{
    private static string FilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CpuTempWidget", "settings.json");

    public Settings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(FilePath)) ?? new Settings();
        }
        catch { /* corrupt or unreadable: fall back to defaults */ }
        return new Settings();
    }

    public void Save(Settings settings)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* never crash the widget over settings */ }
    }
}
