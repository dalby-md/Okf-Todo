using Photino.Okf_Todo.Data;

namespace Okf_Todo.Tests;

public sealed class DatabasePathProviderTests
{
    [Fact]
    public void ResolveDatabasePath_UsesWindowsLocalApplicationData()
    {
        var localApplicationData = Path.GetFullPath("windows-app-data");

        var result = DatabasePathProvider.ResolveDatabasePath(
            DatabasePlatform.Windows,
            localApplicationData,
            Path.GetFullPath("home"),
            null);

        Assert.Equal(Path.Combine(localApplicationData, "Okf-Todo", "okf-todo.db"), result);
    }

    [Fact]
    public void ResolveDatabasePath_UsesMacOSApplicationSupport()
    {
        var userProfile = Path.GetFullPath("mac-home");

        var result = DatabasePathProvider.ResolveDatabasePath(
            DatabasePlatform.MacOS,
            Path.GetFullPath("local-data"),
            userProfile,
            null);

        Assert.Equal(
            Path.Combine(userProfile, "Library", "Application Support", "Okf-Todo", "okf-todo.db"),
            result);
    }

    [Fact]
    public void ResolveDatabasePath_UsesLinuxXdgDataHomeWhenFullyQualified()
    {
        var xdgDataHome = Path.GetFullPath("xdg-data");

        var result = DatabasePathProvider.ResolveDatabasePath(
            DatabasePlatform.Linux,
            Path.GetFullPath("local-data"),
            Path.GetFullPath("linux-home"),
            xdgDataHome);

        Assert.Equal(Path.Combine(xdgDataHome, "Okf-Todo", "okf-todo.db"), result);
    }

    [Fact]
    public void ResolveDatabasePath_UsesLinuxHomeFallbackForRelativeXdgPath()
    {
        var userProfile = Path.GetFullPath("linux-home");

        var result = DatabasePathProvider.ResolveDatabasePath(
            DatabasePlatform.Linux,
            Path.GetFullPath("local-data"),
            userProfile,
            "relative-xdg-data");

        Assert.Equal(
            Path.Combine(userProfile, ".local", "share", "Okf-Todo", "okf-todo.db"),
            result);
    }
}
