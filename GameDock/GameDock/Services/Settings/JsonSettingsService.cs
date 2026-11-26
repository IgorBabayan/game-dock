using System.Text.Json;
using System.Text.Json.Serialization;
using GameDock.Services.Platform;

namespace GameDock.Services.Settings;

/// <summary>
/// Stores settings as JSON in <c>settings.json</c> under <see cref="PlatformInfo.SettingsFolder"/>
/// (<c>%APPDATA%\GameDock</c> on Windows, as before).
/// </summary>
public sealed class JsonSettingsService : ISettingsService
{
    private readonly string _filePath = Path.Combine(PlatformInfo.SettingsFolder, "settings.json");

    public JsonSettingsService()
    {
        Current = Load();
    }

    public AppSettings Current { get; private set; }

    public event EventHandler<AppSettings>? SettingsChanged;

    public void Save(AppSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);

        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);

        // Write to a temp file first so a crash mid-write never leaves a corrupted settings file.
        var tempPath = _filePath + ".tmp";
        File.WriteAllText(tempPath, JsonSerializer.Serialize(settings, SettingsJsonContext.Default.AppSettings));
        File.Move(tempPath, _filePath, overwrite: true);

        Current = settings;
        SettingsChanged?.Invoke(this, settings);
    }

    private AppSettings Load()
    {
        try
        {
            if (!File.Exists(_filePath))
                return new AppSettings();

            return JsonSerializer.Deserialize(File.ReadAllText(_filePath), SettingsJsonContext.Default.AppSettings)
                   ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Unreadable or corrupted settings must not prevent the app from starting.
            return new AppSettings();
        }
    }
}

/// <summary>
/// Compile-time JSON metadata for the settings file: no reflection-based serializer warm-up on startup.
/// Same format as before (indented, enums as names), so existing settings files keep working.
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true, UseStringEnumConverter = true)]
[JsonSerializable(typeof(AppSettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;
