using Photino.NET;
using Photino.NET.Server;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Photino.Okf_Todo.Bridge;
using Photino.Okf_Todo.Data;
using Photino.Okf_Todo.Services;
using System.Drawing;

namespace Photino.Okf_Todo
{
    internal class Program
    {
        private static readonly Size DefaultWindowSize = new(1280, 800);

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
                    logger.LogInformation("Ensuring SQLite database schema.");
                    EnsureDatabase(scope.ServiceProvider, logger);
                    logger.LogInformation("SQLite database is ready at {DatabasePath}.", DatabasePathProvider.GetDatabasePath());
                    logger.LogInformation("Seeding lookup values.");
                    scope.ServiceProvider.GetRequiredService<LookupSeedService>()
                        .SeedAsync()
                        .GetAwaiter()
                        .GetResult();
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, "Database initialization failed.");
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

            var windowPreference = LoadWindowPreference(services, startupLogger);
            var window = new PhotinoWindow()
                .SetTitle("OKF Todo")
                .SetUseOsDefaultSize(false)
                .SetSize(GetStartupWindowSize(windowPreference))
                .SetResizable(true)
                .RegisterCustomSchemeHandler(
                    "app",
                    (object sender, string scheme, string url, out string contentType) =>
                        OpenCustomSchemeStream(services, startupLogger, url, out contentType))
                .RegisterWebMessageReceivedHandler(
                    (object? sender, string message) =>
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
                    });

            ApplyStartupWindowPlacement(window, windowPreference);
            window.WindowClosing += (_, _) =>
            {
                SaveWindowPreference(services, startupLogger, window);
                return false;
            };
            window.Load(appUrl);

            window.WaitForClose();
        }

        private static WindowPreferenceDto LoadWindowPreference(IServiceProvider services, ILogger logger)
        {
            try
            {
                using var scope = services.CreateScope();
                return scope.ServiceProvider.GetRequiredService<AppPreferenceService>()
                    .GetWindowPreferenceAsync(CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Could not load window preference.");
                return new WindowPreferenceDto(null, null, null, null, true);
            }
        }

        private static void SaveWindowPreference(IServiceProvider services, ILogger logger, PhotinoWindow window)
        {
            try
            {
                var isMaximized = window.Maximized;
                var request = isMaximized
                    ? new WindowPreferenceSaveRequest(null, null, null, null, true)
                    : new WindowPreferenceSaveRequest(window.Left, window.Top, window.Width, window.Height, false);

                using var scope = services.CreateScope();
                scope.ServiceProvider.GetRequiredService<AppPreferenceService>()
                    .SaveWindowPreferenceAsync(request, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Could not save window preference.");
            }
        }

        private static Size GetStartupWindowSize(WindowPreferenceDto preference)
        {
            return preference is { IsMaximized: false, Width: not null, Height: not null }
                ? new Size(preference.Width.Value, preference.Height.Value)
                : DefaultWindowSize;
        }

        private static void ApplyStartupWindowPlacement(PhotinoWindow window, WindowPreferenceDto preference)
        {
            if (preference is { IsMaximized: false, Left: not null, Top: not null })
            {
                window.SetLocation(new Point(preference.Left.Value, preference.Top.Value));
                return;
            }

            window.Center();

            if (preference.IsMaximized)
            {
                window.SetMaximized(true);
            }
        }

        private static Stream OpenCustomSchemeStream(
            IServiceProvider services,
            ILogger logger,
            string url,
            out string contentType)
        {
            contentType = "text/plain";

            try
            {
                if (!TryGetImageId(url, out var imageId))
                {
                    logger.LogWarning("Rejected unsupported app custom scheme URL {Url}.", url);
                    return Stream.Null;
                }

                using var scope = services.CreateScope();
                var image = scope.ServiceProvider.GetRequiredService<ImageService>()
                    .GetAsync(imageId, CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();

                contentType = image.MimeType;
                return new MemoryStream(Convert.FromBase64String(image.Base64Data));
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Could not serve custom scheme URL {Url}.", url);
                contentType = "text/plain";
                return Stream.Null;
            }
        }

        private static bool TryGetImageId(string url, out int imageId)
        {
            imageId = 0;

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                return false;
            }

            if (!string.Equals(uri.Scheme, "app", StringComparison.OrdinalIgnoreCase)
                || !string.Equals(uri.Host, "image", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            var idText = uri.AbsolutePath.Trim('/');
            return int.TryParse(idText, out imageId);
        }

        private static void EnsureDatabase(IServiceProvider services, ILogger logger)
        {
            RepairIncompleteDatabase(logger);

            var dbContext = services.GetRequiredService<AppDbContext>();
            dbContext.Database.EnsureCreated();
            SqliteSchemaMaintenance.CleanupCurrentDatabase(DatabasePathProvider.GetDatabasePath(), logger);
        }

        private static void RepairIncompleteDatabase(ILogger logger)
        {
            var databasePath = DatabasePathProvider.GetDatabasePath();
            if (!File.Exists(databasePath)
                || SqliteSchemaMaintenance.HasTable(databasePath, "Issues")
                || !SqliteSchemaMaintenance.HasTable(databasePath, "__EFMigrationsHistory"))
            {
                return;
            }

            logger.LogWarning("Removing incomplete SQLite database at {DatabasePath}.", databasePath);
            File.Delete(databasePath);
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
            services.AddSingleton<IAppPreferencePathProvider, AppPreferencePathProvider>();
            services.AddScoped<LookupSeedService>();
            services.AddScoped<TaskLifecycleService>();
            services.AddScoped<TaskService>();
            services.AddScoped<TaskAttachmentService>();
            services.AddScoped<TaskChecklistService>();
            services.AddScoped<TaskRelationService>();
            services.AddScoped<AppPreferenceService>();
            services.AddScoped<IssueService>();
            services.AddScoped<ImageService>();
            services.AddSingleton<BridgeMessageHandler>();

            return services.BuildServiceProvider();
        }
    }
}
