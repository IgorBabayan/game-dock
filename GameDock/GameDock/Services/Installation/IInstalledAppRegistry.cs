using GameDock.Services.Catalog;

namespace GameDock.Services.Installation;

/// <summary>Finds apps installed by their Inno Setup installers.</summary>
public interface IInstalledAppRegistry
{
    /// <summary>Returns the installation of <paramref name="app"/>, or <c>null</c> if it is not installed.</summary>
    InstalledApp? Find(ManagedApp app);
}
