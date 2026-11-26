using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using GameDock.Services.Catalog;
using GameDock.Services.GitHub;
using GameDock.Services.Platform;

namespace GameDock.Services.Installation;

/// <summary>
/// Linux / macOS: apps installed by <see cref="PortableAppInstaller"/> live in
/// <c>&lt;PortableAppsFolder&gt;/&lt;app id&gt;/</c> with a small manifest that records the version and entry point.
/// </summary>
public sealed class PortableAppRegistry : IInstalledAppRegistry
{
    internal const string ManifestFileName = ".gamedock-install.json";

    public InstalledApp? Find(ManagedApp app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var folder = GetInstallFolder(app);
        var manifestPath = Path.Combine(folder, ManifestFileName);

        try
        {
            if (!File.Exists(manifestPath))
                return null;

            var manifest = JsonSerializer.Deserialize(File.ReadAllText(manifestPath), PortableJsonContext.Default.PortableManifest);
            if (manifest?.EntryPoint is not { Length: > 0 } entryPoint)
                return null;

            var entryPath = Path.GetFullPath(Path.Combine(folder, entryPoint));

            // The manifest can outlive the files (e.g. deleted by hand): treat as not installed.
            if (!File.Exists(entryPath) && !Directory.Exists(entryPath))
                return null;

            Version? version = VersionHelper.TryParseTag(manifest.Version, out var parsed) ? parsed : null;
            return new InstalledApp(version, folder, UninstallerPath: null, IsAllUsers: false, EntryPoint: entryPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            Debug.WriteLine($"Could not read {manifestPath}: {ex.Message}");
            return null;
        }
    }

    internal static string GetInstallFolder(ManagedApp app) => Path.Combine(PlatformInfo.PortableAppsFolder, app.Id);
}

/// <summary>Written next to a portable install.</summary>
/// <param name="Version">Release tag the files came from.</param>
/// <param name="EntryPoint">Executable or <c>.app</c> bundle, relative to the install folder.</param>
/// <param name="InstalledAt">When App Launcher installed it.</param>
internal sealed record PortableManifest(string? Version, string? EntryPoint, DateTimeOffset InstalledAt);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(PortableManifest))]
internal sealed partial class PortableJsonContext : JsonSerializerContext;
