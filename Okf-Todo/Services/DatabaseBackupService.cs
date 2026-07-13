using System.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Photino.Okf_Todo.Data;

namespace Photino.Okf_Todo.Services;

public sealed class DatabaseBackupService(
    AppDbContext dbContext,
    IBackupDestinationPicker destinationPicker,
    ILogger<DatabaseBackupService> logger)
{
    public async Task<DatabaseBackupResult> CreateAsync(CancellationToken cancellationToken)
    {
        var suggestedFileName = $"okf-todo-backup-{DateTime.Now:yyyyMMdd-HHmmss}.db";
        var selectedPath = await destinationPicker.PickAsync(suggestedFileName, cancellationToken);
        if (string.IsNullOrWhiteSpace(selectedPath))
        {
            return new DatabaseBackupResult(true, null, null);
        }

        var destinationPath = Path.GetFullPath(selectedPath);
        var destinationDirectory = Path.GetDirectoryName(destinationPath)
            ?? throw new ValidationException("Backup destination is invalid.", "destinationPath");
        Directory.CreateDirectory(destinationDirectory);

        var temporaryPath = Path.Combine(
            destinationDirectory,
            $".{Path.GetFileName(destinationPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            var sourceConnection = GetSourceConnection();
            RejectSourceAsDestination(sourceConnection, destinationPath);

            var openedSourceConnection = sourceConnection.State != ConnectionState.Open;
            if (openedSourceConnection)
            {
                await sourceConnection.OpenAsync(cancellationToken);
            }

            try
            {
                await CreateAndValidateBackupAsync(sourceConnection, temporaryPath, cancellationToken);
            }
            finally
            {
                if (openedSourceConnection)
                {
                    await sourceConnection.CloseAsync();
                }
            }

            File.Move(temporaryPath, destinationPath, true);
            var fileSize = new FileInfo(destinationPath).Length;

            logger.LogInformation(
                "Created SQLite database backup at {BackupPath} with {BackupSize} bytes.",
                destinationPath,
                fileSize);

            return new DatabaseBackupResult(false, destinationPath, fileSize);
        }
        catch (ValidationException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Could not create SQLite database backup at {BackupPath}.", destinationPath);
            throw new BridgeException("BackupFailed", "Could not create the database backup.");
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }
    }

    private SqliteConnection GetSourceConnection()
    {
        return dbContext.Database.GetDbConnection() as SqliteConnection
            ?? throw new InvalidOperationException("The application database is not a SQLite database.");
    }

    private static void RejectSourceAsDestination(SqliteConnection sourceConnection, string destinationPath)
    {
        if (string.IsNullOrWhiteSpace(sourceConnection.DataSource)
            || string.Equals(sourceConnection.DataSource, ":memory:", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var sourcePath = Path.GetFullPath(sourceConnection.DataSource);
        if (string.Equals(sourcePath, destinationPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationException(
                "Choose a backup file other than the active database.",
                "destinationPath");
        }
    }

    private static async Task CreateAndValidateBackupAsync(
        SqliteConnection sourceConnection,
        string temporaryPath,
        CancellationToken cancellationToken)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = temporaryPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString();

        await using var backupConnection = new SqliteConnection(connectionString);
        await backupConnection.OpenAsync(cancellationToken);
        sourceConnection.BackupDatabase(backupConnection);

        await using var integrityCommand = backupConnection.CreateCommand();
        integrityCommand.CommandText = "PRAGMA quick_check;";
        var integrityResult = Convert.ToString(await integrityCommand.ExecuteScalarAsync(cancellationToken));
        if (!string.Equals(integrityResult, "ok", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException($"SQLite backup integrity check failed: {integrityResult}");
        }

        await using var schemaCommand = backupConnection.CreateCommand();
        schemaCommand.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'TaskItems';";
        var taskTableCount = Convert.ToInt64(await schemaCommand.ExecuteScalarAsync(cancellationToken));
        if (taskTableCount != 1)
        {
            throw new InvalidDataException("SQLite backup does not contain the task table.");
        }
    }
}

public sealed record DatabaseBackupResult(bool Cancelled, string? FilePath, long? FileSize);
