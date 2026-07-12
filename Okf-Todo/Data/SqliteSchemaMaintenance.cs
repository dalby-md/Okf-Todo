using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Photino.Okf_Todo.Data;

public static class SqliteSchemaMaintenance
{
    public static void CleanupCurrentDatabase(string databasePath, ILogger logger)
    {
        if (!File.Exists(databasePath))
        {
            return;
        }

        using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        connection.Open();

        if (HasTable(connection, "TaskTags") && !HasColumn(connection, "TaskTags", "Value"))
        {
            DropTableIfExists(connection, "TaskTaskTags");
            DropTableIfExists(connection, "TaskTags");
        }

        EnsureTagTables(connection);

        if (HasTable(connection, "TaskStakeholders"))
        {
            logger.LogInformation("Dropping removed task stakeholder tables.");
            Execute(connection, "DROP TABLE \"TaskStakeholders\"");
        }

        DropTableIfExists(connection, "StakeholderTypes");
        DropTableIfExists(connection, "StakeholderRoles");

        if (HasColumn(connection, "TaskAttachments", "AttachmentKindId"))
        {
            logger.LogInformation("Removing attachment classification from task attachments.");
            RebuildTaskAttachments(connection);
        }
        else
        {
            DropIndexIfExists(connection, "IX_TaskAttachments_AttachmentKindId");
        }

        DropTableIfExists(connection, "AttachmentKinds");

        if (HasTable(connection, "TaskLogEntries") && HasTable(connection, "TaskLogTypes"))
        {
            Execute(connection, """
                DELETE FROM "TaskLogEntries"
                WHERE "TaskLogTypeId" IN (
                    SELECT "Id"
                    FROM "TaskLogTypes"
                    WHERE "Code" IN ('STAKEHOLDER_ADDED', 'STAKEHOLDER_REMOVED', 'TAG_ADDED', 'TAG_REMOVED')
                )
                """);

            Execute(connection, """
                DELETE FROM "TaskLogTypes"
                WHERE "Code" IN ('STAKEHOLDER_ADDED', 'STAKEHOLDER_REMOVED', 'TAG_ADDED', 'TAG_REMOVED')
                """);
        }

        DropIfExists(connection, "TR_WaitingForTypes_IsSelected_AfterInsert", "TRIGGER");
        DropIfExists(connection, "TR_WaitingForTypes_IsSelected_AfterUpdate", "TRIGGER");

        if (HasColumn(connection, "TaskWaitingFors", "WaitingForTypeId"))
        {
            logger.LogInformation("Removing stale WaitingForTypeId column from TaskWaitingFors.");
            RebuildTaskWaitingFors(connection);
        }
        else
        {
            DropIndexIfExists(connection, "IX_TaskWaitingFors_WaitingForTypeId");
        }

        if (HasTable(connection, "WaitingForTypes"))
        {
            logger.LogInformation("Dropping stale WaitingForTypes lookup table.");
            Execute(connection, "DROP TABLE \"WaitingForTypes\"");
        }
    }

    public static bool HasTable(string databasePath, string tableName)
    {
        if (!File.Exists(databasePath))
        {
            return false;
        }

        using var connection = new SqliteConnection($"Data Source={databasePath};Pooling=False");
        connection.Open();
        return HasTable(connection, tableName);
    }

    private static void RebuildTaskWaitingFors(SqliteConnection connection)
    {
        DropIndexIfExists(connection, "IX_TaskWaitingFors_WaitingForTypeId");
        Execute(connection, "DROP TABLE IF EXISTS \"TaskWaitingFors_rebuild\"");

        Execute(connection, """
            CREATE TABLE "TaskWaitingFors_rebuild" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_TaskWaitingFors" PRIMARY KEY AUTOINCREMENT,
                "TaskId" INTEGER NOT NULL,
                "Label" TEXT NOT NULL,
                "WaitingSince" TEXT NOT NULL,
                "ResolvedAt" TEXT NULL,
                "CreatedAt" TEXT NOT NULL,
                "UpdatedAt" TEXT NOT NULL,
                CONSTRAINT "FK_TaskWaitingFors_TaskItems_TaskId" FOREIGN KEY ("TaskId") REFERENCES "TaskItems" ("Id") ON DELETE CASCADE
            )
            """);

        Execute(connection, """
            INSERT INTO "TaskWaitingFors_rebuild" (
                "Id",
                "TaskId",
                "Label",
                "WaitingSince",
                "ResolvedAt",
                "CreatedAt",
                "UpdatedAt"
            )
            SELECT
                "Id",
                "TaskId",
                "Label",
                "WaitingSince",
                "ResolvedAt",
                "CreatedAt",
                "UpdatedAt"
            FROM "TaskWaitingFors"
            """);

        Execute(connection, "DROP TABLE \"TaskWaitingFors\"");
        Execute(connection, "ALTER TABLE \"TaskWaitingFors_rebuild\" RENAME TO \"TaskWaitingFors\"");
        Execute(connection, "CREATE UNIQUE INDEX \"IX_TaskWaitingFors_TaskId\" ON \"TaskWaitingFors\" (\"TaskId\") WHERE ResolvedAt IS NULL");
    }

    private static void RebuildTaskAttachments(SqliteConnection connection)
    {
        DropIndexIfExists(connection, "IX_TaskAttachments_AttachmentKindId");
        Execute(connection, "DROP TABLE IF EXISTS \"TaskAttachments_rebuild\"");

        Execute(connection, """
            CREATE TABLE "TaskAttachments_rebuild" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_TaskAttachments" PRIMARY KEY AUTOINCREMENT,
                "TaskId" INTEGER NOT NULL,
                "FileName" TEXT NOT NULL,
                "ContentType" TEXT NULL,
                "FileSize" INTEGER NOT NULL,
                "Sha256Hash" TEXT NULL,
                "ContentBlob" BLOB NOT NULL,
                "Description" TEXT NULL,
                "CreatedAt" TEXT NOT NULL,
                CONSTRAINT "FK_TaskAttachments_TaskItems_TaskId" FOREIGN KEY ("TaskId") REFERENCES "TaskItems" ("Id") ON DELETE CASCADE
            )
            """);

        Execute(connection, """
            INSERT INTO "TaskAttachments_rebuild" (
                "Id", "TaskId", "FileName", "ContentType", "FileSize",
                "Sha256Hash", "ContentBlob", "Description", "CreatedAt"
            )
            SELECT
                "Id", "TaskId", "FileName", "ContentType", "FileSize",
                "Sha256Hash", "ContentBlob", "Description", "CreatedAt"
            FROM "TaskAttachments"
            """);

        Execute(connection, "DROP TABLE \"TaskAttachments\"");
        Execute(connection, "ALTER TABLE \"TaskAttachments_rebuild\" RENAME TO \"TaskAttachments\"");
        Execute(connection, "CREATE INDEX \"IX_TaskAttachments_TaskId\" ON \"TaskAttachments\" (\"TaskId\")");
    }

    private static void EnsureTagTables(SqliteConnection connection)
    {
        Execute(connection, """
            CREATE TABLE IF NOT EXISTS "TaskTags" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_TaskTags" PRIMARY KEY AUTOINCREMENT,
                "Value" TEXT COLLATE NOCASE NOT NULL
            )
            """);
        Execute(connection, "CREATE UNIQUE INDEX IF NOT EXISTS \"IX_TaskTags_Value\" ON \"TaskTags\" (\"Value\")");

        Execute(connection, """
            CREATE TABLE IF NOT EXISTS "TaskTaskTags" (
                "TaskId" INTEGER NOT NULL,
                "TaskTagId" INTEGER NOT NULL,
                CONSTRAINT "PK_TaskTaskTags" PRIMARY KEY ("TaskId", "TaskTagId"),
                CONSTRAINT "FK_TaskTaskTags_TaskItems_TaskId" FOREIGN KEY ("TaskId") REFERENCES "TaskItems" ("Id") ON DELETE CASCADE,
                CONSTRAINT "FK_TaskTaskTags_TaskTags_TaskTagId" FOREIGN KEY ("TaskTagId") REFERENCES "TaskTags" ("Id") ON DELETE CASCADE
            )
            """);
        Execute(connection, "CREATE INDEX IF NOT EXISTS \"IX_TaskTaskTags_TaskTagId\" ON \"TaskTaskTags\" (\"TaskTagId\")");
    }


    private static bool HasTable(SqliteConnection connection, string tableName)
    {
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $name";
        command.Parameters.AddWithValue("$name", tableName);
        return Convert.ToInt32(command.ExecuteScalar()) > 0;
    }

    private static bool HasColumn(SqliteConnection connection, string tableName, string columnName)
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

    private static void DropIfExists(SqliteConnection connection, string name, string objectType)
    {
        Execute(connection, $"DROP {objectType} IF EXISTS \"{name}\"");
    }

    private static void DropIndexIfExists(SqliteConnection connection, string name)
    {
        Execute(connection, $"DROP INDEX IF EXISTS \"{name}\"");
    }

    private static void DropTableIfExists(SqliteConnection connection, string name)
    {
        Execute(connection, $"DROP TABLE IF EXISTS \"{name}\"");
    }

    private static void Execute(SqliteConnection connection, string commandText)
    {
        using var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.ExecuteNonQuery();
    }
}
