using System.Text.Json;
using GameDock.Services.Catalog;
using GameDock.Services.Installation;
using GameDock.Tests.Support;

namespace GameDock.Tests.Services;

/// <summary>Uses a unique app id, so only a throw-away folder under the portable apps folder is touched.</summary>
public class PortableAppRegistryTests : IDisposable
{
    private readonly ManagedApp _app = TestApps.Create(id: $"gamedock-tests-{Guid.NewGuid():N}");
    private readonly PortableAppRegistry _registry = new();
    private readonly string _folder;

    public PortableAppRegistryTests()
    {
        _folder = PortableAppRegistry.GetInstallFolder(_app);
    }

    public void Dispose()
    {
        if (Directory.Exists(_folder))
            Directory.Delete(_folder, recursive: true);
    }

    private void WriteManifest(string? version, string? entryPoint)
    {
        Directory.CreateDirectory(_folder);
        var manifest = new PortableManifest(version, entryPoint, DateTimeOffset.UtcNow);
        File.WriteAllText(
            Path.Combine(_folder, PortableAppRegistry.ManifestFileName),
            JsonSerializer.Serialize(manifest, PortableJsonContext.Default.PortableManifest));
    }

    private string CreateEntryFile(string name = "Test.App")
    {
        Directory.CreateDirectory(_folder);
        var path = Path.Combine(_folder, name);
        File.WriteAllText(path, "#!/bin/sh");
        return path;
    }

    [Fact]
    public void GetInstallFolder_IsAppIdUnderPortableAppsFolder()
    {
        Assert.Equal(_app.Id, Path.GetFileName(_folder));
        Assert.Equal(GameDock.Services.Platform.PlatformInfo.PortableAppsFolder, Path.GetDirectoryName(_folder));
    }

    [Fact]
    public void Find_NotInstalled_ReturnsNull()
    {
        Assert.Null(_registry.Find(_app));
    }

    [Fact]
    public void Find_ValidInstall_ReturnsVersionAndEntryPoint()
    {
        var entry = CreateEntryFile();
        WriteManifest("v1.2.3", "Test.App");

        var installed = _registry.Find(_app);

        Assert.NotNull(installed);
        Assert.Equal(new Version(1, 2, 3), installed.Version);
        Assert.Equal(_folder, installed.InstallFolder);
        Assert.Equal(Path.GetFullPath(entry), installed.EntryPoint);
        Assert.Null(installed.UninstallerPath);
        Assert.False(installed.IsAllUsers);
        Assert.Equal(Path.GetFullPath(entry), installed.GetExePath(_app.ExeName));
    }

    [Fact]
    public void Find_AppBundleDirectory_IsAccepted()
    {
        Directory.CreateDirectory(Path.Combine(_folder, "Test App.app"));
        WriteManifest("1.0.0", "Test App.app");

        Assert.NotNull(_registry.Find(_app));
    }

    [Fact]
    public void Find_UnparseableVersion_ReturnsInstallWithoutVersion()
    {
        CreateEntryFile();
        WriteManifest("nightly", "Test.App");

        var installed = _registry.Find(_app);

        Assert.NotNull(installed);
        Assert.Null(installed.Version);
    }

    [Fact]
    public void Find_EntryPointDeleted_ReturnsNull()
    {
        WriteManifest("1.0.0", "Test.App");

        Assert.Null(_registry.Find(_app));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Find_ManifestWithoutEntryPoint_ReturnsNull(string? entryPoint)
    {
        CreateEntryFile();
        WriteManifest("1.0.0", entryPoint);

        Assert.Null(_registry.Find(_app));
    }

    [Fact]
    public void Find_CorruptedManifest_ReturnsNull()
    {
        CreateEntryFile();
        File.WriteAllText(Path.Combine(_folder, PortableAppRegistry.ManifestFileName), "{ broken");

        Assert.Null(_registry.Find(_app));
    }

    [Fact]
    public void Manifest_UsesCamelCase()
    {
        var json = JsonSerializer.Serialize(
            new PortableManifest("1.0.0", "App", DateTimeOffset.UnixEpoch), PortableJsonContext.Default.PortableManifest);

        Assert.Contains("\"version\"", json);
        Assert.Contains("\"entryPoint\"", json);
        Assert.Contains("\"installedAt\"", json);
    }

    [Fact]
    public void Find_Null_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => _registry.Find(null!));
    }
}
