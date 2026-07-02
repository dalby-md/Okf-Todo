namespace Photino.Okf_Todo.Data;

public static class DatabasePathProvider
{
    public static string GetDatabasePath()
    {
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var databaseDirectory = Path.Combine(appDataPath, "Okf-Todo");

        Directory.CreateDirectory(databaseDirectory);

        return Path.Combine(databaseDirectory, "okf-todo.db");
    }
}
