using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Photino.Okf_Todo.Data;
using Photino.Okf_Todo.Services;

namespace Okf_Todo.Tests;

public sealed class DatabaseBackupServiceTests
{
    [Fact]
    public async Task CreateAsync_CreatesValidatedCopyWithoutChangingSource()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Okf-Todo.Tests", Guid.NewGuid().ToString("N"));
        var sourcePath = Path.Combine(directory, "source.db");
        var backupPath = Path.Combine(directory, "backup.db");
        Directory.CreateDirectory(directory);

        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite($"Data Source={sourcePath};Pooling=False")
                .Options;
            await using var dbContext = new AppDbContext(options);
            await dbContext.Database.EnsureCreatedAsync();
            dbContext.Issues.Add(new Issue
            {
                Title = "Backed up issue",
                Status = "Open",
                CreatedUtc = DateTime.UtcNow,
                ModifiedUtc = DateTime.UtcNow,
                BodyHtml = "<p>Backup</p>",
                BodyMarkdown = "Backup",
                EditorMode = "html"
            });
            await dbContext.SaveChangesAsync();

            var picker = new TestBackupDestinationPicker(backupPath);
            using var loggerFactory = LoggerFactory.Create(_ => { });
            var service = new DatabaseBackupService(
                dbContext,
                picker,
                loggerFactory.CreateLogger<DatabaseBackupService>());

            var result = await service.CreateAsync(CancellationToken.None);

            Assert.False(result.Cancelled);
            Assert.Equal(Path.GetFullPath(backupPath), result.FilePath);
            Assert.True(result.FileSize > 0);
            Assert.True(File.Exists(backupPath));
            Assert.Equal(1, await dbContext.Issues.CountAsync());

            await using var backupConnection = new SqliteConnection($"Data Source={backupPath};Mode=ReadOnly;Pooling=False");
            await backupConnection.OpenAsync();
            await using var command = backupConnection.CreateCommand();
            command.CommandText = "SELECT Title FROM Issues LIMIT 1;";
            Assert.Equal("Backed up issue", await command.ExecuteScalarAsync());
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, true);
            }
        }
    }

    [Fact]
    public async Task CreateAsync_WhenDialogIsCancelledDoesNotCreateBackup()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=:memory:")
            .Options;
        await using var dbContext = new AppDbContext(options);
        using var loggerFactory = LoggerFactory.Create(_ => { });
        var service = new DatabaseBackupService(
            dbContext,
            new TestBackupDestinationPicker(null),
            loggerFactory.CreateLogger<DatabaseBackupService>());

        var result = await service.CreateAsync(CancellationToken.None);

        Assert.True(result.Cancelled);
        Assert.Null(result.FilePath);
        Assert.Null(result.FileSize);
    }

    private sealed class TestBackupDestinationPicker(string? path) : IBackupDestinationPicker
    {
        public Task<string?> PickAsync(string suggestedFileName, CancellationToken cancellationToken)
        {
            return Task.FromResult(path);
        }
    }
}
