using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Photino.Okf_Todo.Data;

namespace Okf_Todo.Tests;

public sealed class SqliteSchemaMaintenanceTests
{
    [Fact]
    public void CleanupCurrentDatabase_RemovesAttachmentClassificationAndPreservesAttachmentContent()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Okf-Todo.Tests", Guid.NewGuid().ToString("N"));
        var databasePath = Path.Combine(directory, "legacy-attachments.db");
        Directory.CreateDirectory(directory);

        try
        {
            CreateLegacyDatabase(databasePath);

            SqliteSchemaMaintenance.CleanupCurrentDatabase(databasePath, NullLogger.Instance);

            using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
            connection.Open();

            Assert.False(TableExists(connection, "AttachmentKinds"));
            Assert.False(ColumnExists(connection, "TaskAttachments", "AttachmentKindId"));

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT FileName, ContentBlob FROM TaskAttachments WHERE Id = 1";
            using var reader = command.ExecuteReader();
            Assert.True(reader.Read());
            Assert.Equal("build.log", reader.GetString(0));
            Assert.Equal(new byte[] { 10, 20, 30 }, (byte[])reader[1]);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static void CreateLegacyDatabase(string databasePath)
    {
        using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA foreign_keys = ON;
            CREATE TABLE TaskItems (Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT);
            CREATE TABLE AttachmentKinds (Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT, Code TEXT NOT NULL);
            CREATE TABLE TaskAttachments (
                Id INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
                TaskId INTEGER NOT NULL,
                FileName TEXT NOT NULL,
                ContentType TEXT NULL,
                FileSize INTEGER NOT NULL,
                Sha256Hash TEXT NULL,
                AttachmentKindId INTEGER NULL,
                ContentBlob BLOB NOT NULL,
                Description TEXT NULL,
                CreatedAt TEXT NOT NULL,
                FOREIGN KEY (TaskId) REFERENCES TaskItems (Id) ON DELETE CASCADE,
                FOREIGN KEY (AttachmentKindId) REFERENCES AttachmentKinds (Id) ON DELETE RESTRICT
            );
            CREATE INDEX IX_TaskAttachments_TaskId ON TaskAttachments (TaskId);
            CREATE INDEX IX_TaskAttachments_AttachmentKindId ON TaskAttachments (AttachmentKindId);
            INSERT INTO TaskItems (Id) VALUES (1);
            INSERT INTO AttachmentKinds (Id, Code) VALUES (1, 'LOG_FILE');
            INSERT INTO TaskAttachments (
                Id, TaskId, FileName, ContentType, FileSize, Sha256Hash,
                AttachmentKindId, ContentBlob, Description, CreatedAt
            ) VALUES (
                1, 1, 'build.log', 'text/plain', 3, NULL,
                1, X'0A141E', NULL, '2026-07-12T12:00:00Z'
            );
            """;
        command.ExecuteNonQuery();
    }

    private static bool TableExists(SqliteConnection connection, string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name";
        command.Parameters.AddWithValue("$name", tableName);
        return Convert.ToInt32(command.ExecuteScalar()) > 0;
    }

    private static bool ColumnExists(SqliteConnection connection, string tableName, string columnName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"PRAGMA table_info(\"{tableName}\")";
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            if (string.Equals(reader.GetString(1), columnName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }
}
