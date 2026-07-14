using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Photino.Okf_Todo.Data;

namespace Okf_Todo.Tests;

public sealed class DatabaseMigrationTests
{
    [Fact]
    public async Task MigrateAsync_CreatesCurrentSchemaAndPreservesExistingData()
    {
        var directory = Path.Combine(Path.GetTempPath(), "Okf-Todo.Tests", Guid.NewGuid().ToString("N"));
        var databasePath = Path.Combine(directory, "migration-test.db");
        Directory.CreateDirectory(directory);

        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(DatabasePathProvider.CreateConnectionString(databasePath, pooling: false))
                .Options;

            await using (var dbContext = new AppDbContext(options))
            {
                await dbContext.Database.MigrateAsync();
                dbContext.Issues.Add(new Issue
                {
                    Title = "Preserved across migration checks",
                    Status = "Open",
                    Priority = 0,
                    CreatedUtc = DateTime.UtcNow,
                    ModifiedUtc = DateTime.UtcNow,
                    BodyHtml = "<p>Migration test</p>",
                    BodyMarkdown = "Migration test",
                    EditorMode = "html"
                });
                await dbContext.SaveChangesAsync();
            }

            await using (var dbContext = new AppDbContext(options))
            {
                await dbContext.Database.MigrateAsync();

                var appliedMigrations = await dbContext.Database.GetAppliedMigrationsAsync();
                Assert.Contains(appliedMigrations, migration => migration.EndsWith("_InitialCreate"));
                Assert.Empty(await dbContext.Database.GetPendingMigrationsAsync());
                Assert.Equal(1, await dbContext.Issues.CountAsync());
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(directory, true);
        }
    }
}
