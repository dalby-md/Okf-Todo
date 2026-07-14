using Photino.NET;
using Photino.NET.Server;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
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
            var isOkfCommandMode = args.Any(argument => string.Equals(
                argument,
                "--okf-command",
                StringComparison.OrdinalIgnoreCase));
            using var singleInstanceMutex = CreateSingleInstanceMutex(isOkfCommandMode, out var isFirstInstance);
            if (!isFirstInstance)
            {
                return;
            }

            Directory.SetCurrentDirectory(AppContext.BaseDirectory);
            var databasePath = ResolveDatabasePath(args, isOkfCommandMode);

            using var services = CreateServices(isOkfCommandMode, databasePath);
            var startupLogger = services.GetRequiredService<ILogger<Program>>();
            startupLogger.LogInformation("Starting OKF-Todo from {BaseDirectory}", AppContext.BaseDirectory);

            using (var scope = services.CreateScope())
            {
                var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

                try
                {
                    logger.LogInformation("Applying pending SQLite database migrations.");
                    MigrateDatabase(scope.ServiceProvider);
                    logger.LogInformation("SQLite database is ready at {DatabasePath}.", databasePath);
                    logger.LogInformation("Seeding lookup values.");
                    scope.ServiceProvider.GetRequiredService<LookupSeedService>()
                        .SeedAsync()
                        .GetAwaiter()
                        .GetResult();

                    if (args.Any(argument => string.Equals(
                        argument,
                        "--seed-sample-tasks",
                        StringComparison.OrdinalIgnoreCase)))
                    {
                        var result = scope.ServiceProvider.GetRequiredService<SampleDataSeeder>()
                            .SeedAsync()
                            .GetAwaiter()
                            .GetResult();
                        logger.LogInformation(
                            "Sample-data command completed with {TaskCount} tasks.",
                            result.TaskCount);
                        return;
                    }
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, "Database initialization failed.");
                    throw;
                }
            }

            if (isOkfCommandMode)
            {
                RunOkfCommand(services, startupLogger);
                return;
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
                .SetTitle("OKF-Todo")
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

            services.GetRequiredService<PhotinoBackupDestinationPicker>().Attach(window);

            ApplyStartupWindowPlacement(window, windowPreference);
            window.WindowClosing += (_, _) =>
            {
                SaveWindowPreference(services, startupLogger, window);
                return false;
            };
            window.Load(appUrl);

            window.WaitForClose();
        }

        private static Mutex? CreateSingleInstanceMutex(bool bypass, out bool isFirstInstance)
        {
            if (bypass)
            {
                isFirstInstance = true;
                return null;
            }

            return new Mutex(true, "OkfTodoSingleInstance", out isFirstInstance);
        }

        private static string ResolveDatabasePath(string[] args, bool isOkfCommandMode)
        {
            const string optionName = "--okf-database-path";
            var optionIndexes = args
                .Select((argument, index) => new { Argument = argument, Index = index })
                .Where(item => string.Equals(item.Argument, optionName, StringComparison.OrdinalIgnoreCase))
                .Select(item => item.Index)
                .ToList();

            if (optionIndexes.Count == 0)
            {
                return DatabasePathProvider.GetDatabasePath();
            }

            if (!isOkfCommandMode)
            {
                throw new ArgumentException($"{optionName} can only be used with --okf-command.");
            }

            if (optionIndexes.Count != 1 || optionIndexes[0] + 1 >= args.Length)
            {
                throw new ArgumentException($"{optionName} requires exactly one absolute file path.");
            }

            var databasePath = args[optionIndexes[0] + 1];
            if (string.IsNullOrWhiteSpace(databasePath) || !Path.IsPathFullyQualified(databasePath))
            {
                throw new ArgumentException($"{optionName} requires an absolute file path.");
            }

            var fullPath = Path.GetFullPath(databasePath);
            var directory = Path.GetDirectoryName(fullPath)
                ?? throw new ArgumentException($"{optionName} requires a file path with a parent directory.");
            Directory.CreateDirectory(directory);
            return fullPath;
        }

        private static void RunOkfCommand(IServiceProvider services, ILogger logger)
        {
            var requestJson = Console.In.ReadToEnd();
            if (string.IsNullOrWhiteSpace(requestJson))
            {
                logger.LogError("The --okf-command option requires one JSON command on standard input.");
                Environment.ExitCode = 2;
                return;
            }

            var result = services.GetRequiredService<OkfCommandRunner>()
                .RunAsync(requestJson)
                .GetAwaiter()
                .GetResult();

            Console.Out.WriteLineAsync(result.ResponseJson).GetAwaiter().GetResult();
            Environment.ExitCode = result.Succeeded ? 0 : 1;
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

        private static void MigrateDatabase(IServiceProvider services)
        {
            var dbContext = services.GetRequiredService<AppDbContext>();
            dbContext.Database.Migrate();
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

        private static ServiceProvider CreateServices(bool logToStandardError, string databasePath)
        {
            var services = new ServiceCollection();

            services.AddLogging(builder =>
            {
                builder.AddSimpleConsole(options =>
                {
                    options.SingleLine = true;
                    options.TimestampFormat = "HH:mm:ss ";
                });
                builder.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning);
            });
            if (logToStandardError)
            {
                services.Configure<ConsoleLoggerOptions>(options =>
                    options.LogToStandardErrorThreshold = LogLevel.Trace);
            }
            services.AddDbContext<AppDbContext>(options =>
                options.UseSqlite(DatabasePathProvider.CreateConnectionString(databasePath)));
            services.AddSingleton<HtmlSanitizerService>();
            services.AddSingleton<IAppPreferencePathProvider, AppPreferencePathProvider>();
            services.AddSingleton<PhotinoBackupDestinationPicker>();
            services.AddSingleton<IBackupDestinationPicker>(serviceProvider =>
                serviceProvider.GetRequiredService<PhotinoBackupDestinationPicker>());
            services.AddScoped<LookupSeedService>();
            services.AddScoped<TaskLifecycleService>();
            services.AddScoped<TaskService>();
            services.AddScoped<TaskAttachmentService>();
            services.AddScoped<TaskChecklistService>();
            services.AddScoped<TaskRelationService>();
            services.AddScoped<AppPreferenceService>();
            services.AddScoped<IssueService>();
            services.AddScoped<ImageService>();
            services.AddScoped<DatabaseBackupService>();
            services.AddScoped<SampleDataSeeder>();
            services.AddSingleton<ApplicationCommandService>();
            services.AddSingleton<BridgeMessageHandler>();
            services.AddSingleton<OkfCommandRunner>();

            return services.BuildServiceProvider();
        }
    }
}
