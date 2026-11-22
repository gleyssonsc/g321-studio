using System.IO;
using System.Text.Json;

namespace G321Studio;

internal sealed record Preset(string Name, float[] Gains, float Preamp)
{
    public override string ToString() => Name;
}

internal sealed class Settings
{
    private static readonly string DirectoryPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "G321 Studio");
    private static readonly string SettingsPath = Path.Combine(DirectoryPath, "settings.json");

    public string PresetName { get; set; } = "Plano";
    public float[] CustomGains { get; set; } = new float[EqParameters.Frequencies.Length];
    public float CustomPreamp { get; set; }
    public float MonitorVolume { get; set; } = 20;
    public string? OutputId { get; set; }
    public string? MicId { get; set; }

    public static Settings Load()
    {
        try
        {
            return JsonSerializer.Deserialize<Settings>(File.ReadAllText(SettingsPath))
                ?? new Settings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new Settings();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(DirectoryPath);
        string temporary = Path.Combine(DirectoryPath, $"settings-{Guid.NewGuid():N}.tmp");
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(this));
            File.Move(temporary, SettingsPath, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}

