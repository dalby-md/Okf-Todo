using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Photino.Okf_Todo.Data;
using Photino.Okf_Todo.Services;

namespace Okf_Todo.Tests;

public sealed class AppPreferenceServiceTests
{
    [Fact]
    public async Task WindowPreference_PreservesRestoredBoundsWhenSavedAsMaximized()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();

        var preferencesDirectory = Path.Combine(
            Path.GetTempPath(),
            "Okf-Todo.Tests",
            Guid.NewGuid().ToString("N"));
        var preferencesPath = Path.Combine(preferencesDirectory, "app-preferences.json");

        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(connection)
                .Options;

            await using var dbContext = new AppDbContext(options);
            var service = new AppPreferenceService(
                dbContext,
                new TestAppPreferencePathProvider(preferencesPath),
                NullLogger<AppPreferenceService>.Instance);

            var initial = await service.GetWindowPreferenceAsync(CancellationToken.None);
            Assert.True(initial.IsMaximized);
            Assert.Null(initial.Left);
            Assert.Null(initial.Top);
            Assert.Null(initial.Width);
            Assert.Null(initial.Height);

            var restored = await service.SaveWindowPreferenceAsync(
                new WindowPreferenceSaveRequest(120, 80, 1440, 900, false),
                CancellationToken.None);

            Assert.False(restored.IsMaximized);
            Assert.Equal(120, restored.Left);
            Assert.Equal(80, restored.Top);
            Assert.Equal(1440, restored.Width);
            Assert.Equal(900, restored.Height);

            var maximized = await service.SaveWindowPreferenceAsync(
                new WindowPreferenceSaveRequest(null, null, null, null, true),
                CancellationToken.None);

            Assert.True(maximized.IsMaximized);
            Assert.Equal(120, maximized.Left);
            Assert.Equal(80, maximized.Top);
            Assert.Equal(1440, maximized.Width);
            Assert.Equal(900, maximized.Height);

            var loaded = await service.GetWindowPreferenceAsync(CancellationToken.None);
            Assert.True(loaded.IsMaximized);
            Assert.Equal(120, loaded.Left);
            Assert.Equal(80, loaded.Top);
            Assert.Equal(1440, loaded.Width);
            Assert.Equal(900, loaded.Height);
        }
        finally
        {
            if (Directory.Exists(preferencesDirectory))
            {
                Directory.Delete(preferencesDirectory, recursive: true);
            }
        }
    }

    private sealed class TestAppPreferencePathProvider(string preferencesPath) : IAppPreferencePathProvider
    {
        public string GetPreferencesPath()
        {
            return preferencesPath;
        }
    }
}
