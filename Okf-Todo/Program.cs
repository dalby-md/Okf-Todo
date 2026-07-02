using Photino.NET;
using Photino.NET.Server;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Photino.Okf_Todo.Bridge;
using Photino.Okf_Todo.Data;
using Photino.Okf_Todo.Services;
using System.Drawing;
using Microsoft.Data.Sqlite;

namespace Photino.Okf_Todo
{
    internal class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            using var singleInstanceMutex = new Mutex(true, "OkfTodoSingleInstance", out var isFirstInstance);
            if (!isFirstInstance)
            {
                return;
            }

            Directory.SetCurrentDirectory(AppContext.BaseDirectory);

            using var services = CreateServices();
            var startupLogger = services.GetRequiredService<ILogger<Program>>();
            startupLogger.LogInformation("Starting OKF Todo from {BaseDirectory}", AppContext.BaseDirectory);

            using (var scope = services.CreateScope())
            {
                var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

                try
                {
                    logger.LogInformation("Applying SQLite migrations.");
                    ApplyMigrations(scope.ServiceProvider, logger);
                    logger.LogInformation("SQLite database is ready at {DatabasePath}.", DatabasePathProvider.GetDatabasePath());
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, "Database migration failed.");
                    throw;
                }
            }

            PhotinoServer
                .CreateStaticFileServer(args, out string baseUrl)
                .RunAsync();

            string readinessUrl = $"{baseUrl}/index.html";
            WaitForStaticFileServerAsync(readinessUrl, startupLogger).GetAwaiter().GetResult();

            string appUrl = $"{readinessUrl}?v={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
            startupLogger.LogInformation("Loading UI from {AppUrl}", appUrl);

            var window = new PhotinoWindow()
                .SetTitle("OKF Todo")
                .SetUseOsDefaultSize(false)
                .SetSize(new Size(800, 600))
                .Center()
                .SetResizable(true)
                .RegisterWebMessageReceivedHandler((object? sender, string message) =>
                {
                    if (sender is not PhotinoWindow window)
                    {
                        return;
                    }

                    _ = Task.Run(async () =>
                    {
                        var handler = services.GetRequiredService<BridgeMessageHandler>();
                        var response = await handler.HandleAsync(message);
                        window.SendWebMessage(response);
                    });
                })
                .Load(appUrl);

            window.WaitForClose();
        }

        private static void ApplyMigrations(IServiceProvider services, ILogger logger)
        {
            try
            {
                services.GetRequiredService<AppDbContext>().Database.Migrate();
            }
            catch (SqliteException exception) when (TryRepairIncompleteInitialMigration(logger, exception))
            {
                services.GetRequiredService<AppDbContext>().Database.Migrate();
            }
        }

        private static bool TryRepairIncompleteInitialMigration(ILogger logger, SqliteException exception)
        {
            if (!exception.Message.Contains("__EFMigrationsHistory", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var databasePath = DatabasePathProvider.GetDatabasePath();
            if (!File.Exists(databasePath) || HasTable(databasePath, "Issues"))
            {
                return false;
            }

            logger.LogWarning(exception, "Removing incomplete initial SQLite database at {DatabasePath}.", databasePath);
            File.Delete(databasePath);

            return true;
        }

        private static bool HasTable(string databasePath, string tableName)
        {
            using var connection = new SqliteConnection($"Data Source={databasePath}");
            connection.Open();

            using var command = connection.CreateCommand();
            command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = $tableName";
            command.Parameters.AddWithValue("$tableName", tableName);

            return Convert.ToInt32(command.ExecuteScalar()) > 0;
        }

        private static async Task WaitForStaticFileServerAsync(string appUrl, ILogger logger)
        {
            using var httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(2)
            };

            Exception? lastException = null;

            for (var attempt = 1; attempt <= 20; attempt++)
            {
                try
                {
                    using var response = await httpClient.GetAsync(appUrl);
                    if (response.IsSuccessStatusCode)
                    {
                        logger.LogInformation("Static UI is ready at {AppUrl}.", appUrl);
                        return;
                    }

                    lastException = new InvalidOperationException(
                        $"Static UI returned HTTP {(int)response.StatusCode} {response.ReasonPhrase}.");
                }
                catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
                {
                    lastException = exception;
                }

                await Task.Delay(TimeSpan.FromMilliseconds(250));
            }

            throw new InvalidOperationException($"Static UI did not become ready at {appUrl}.", lastException);
        }

        private static ServiceProvider CreateServices()
        {
            var services = new ServiceCollection();
            var databasePath = DatabasePathProvider.GetDatabasePath();

            services.AddLogging(builder =>
            {
                builder.AddSimpleConsole(options =>
                {
                    options.SingleLine = true;
                    options.TimestampFormat = "HH:mm:ss ";
                });
                builder.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning);
            });
            services.AddDbContext<AppDbContext>(options => options.UseSqlite($"Data Source={databasePath}"));
            services.AddSingleton<HtmlSanitizerService>();
            services.AddScoped<IssueService>();
            services.AddScoped<ImageService>();
            services.AddSingleton<BridgeMessageHandler>();

            return services.BuildServiceProvider();
        }
    }
}
