using Microsoft.Data.Sqlite;

namespace Photino.Okf_Todo.Data;

public static class DatabasePathProvider
{
    public static string GetDatabasePath()
    {
        var databasePath = ResolveDatabasePath(
            GetCurrentPlatform(),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetEnvironmentVariable("XDG_DATA_HOME"));
        var databaseDirectory = Path.GetDirectoryName(databasePath)
            ?? throw new InvalidOperationException("The database path has no parent directory.");

        Directory.CreateDirectory(databaseDirectory);

        return databasePath;
    }

    public static string GetConnectionString()
    {
        return CreateConnectionString(GetDatabasePath());
    }

    public static string CreateConnectionString(string databasePath, bool pooling = true)
    {
        return new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            ForeignKeys = true,
            Pooling = pooling
        }.ToString();
    }

    internal static string ResolveDatabasePath(
        DatabasePlatform platform,
        string localApplicationDataPath,
        string userProfilePath,
        string? xdgDataHome)
    {
        var dataRoot = platform switch
        {
            DatabasePlatform.Windows => RequirePath(localApplicationDataPath, "local application data"),
            DatabasePlatform.MacOS => Path.Combine(
                RequirePath(userProfilePath, "user profile"),
                "Library",
                "Application Support"),
            DatabasePlatform.Linux when !string.IsNullOrWhiteSpace(xdgDataHome)
                && Path.IsPathFullyQualified(xdgDataHome) => xdgDataHome,
            DatabasePlatform.Linux => Path.Combine(
                RequirePath(userProfilePath, "user profile"),
                ".local",
                "share"),
            _ => RequirePath(localApplicationDataPath, "local application data")
        };

        return Path.Combine(dataRoot, "Okf-Todo", "okf-todo.db");
    }

    private static DatabasePlatform GetCurrentPlatform()
    {
        if (OperatingSystem.IsWindows())
        {
            return DatabasePlatform.Windows;
        }

        if (OperatingSystem.IsMacOS())
        {
            return DatabasePlatform.MacOS;
        }

        if (OperatingSystem.IsLinux())
        {
            return DatabasePlatform.Linux;
        }

        return DatabasePlatform.Other;
    }

    private static string RequirePath(string path, string pathDescription)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException($"The operating system did not provide a {pathDescription} path.");
        }

        return path;
    }
}

internal enum DatabasePlatform
{
    Windows,
    MacOS,
    Linux,
    Other
}
