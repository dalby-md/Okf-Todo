using System.Net;
using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Playwright;
using Photino.Okf_Todo.Bridge;
using Photino.Okf_Todo.Data;
using Photino.Okf_Todo.Services;

namespace Okf_Todo.UiTests;

public sealed class NewTaskDialogUiTests
{
    private const string BridgeAdapterScript = """
        (() => {
          const listeners = [];
          window.chrome = window.chrome || {};
          window.chrome.webview = {
            addEventListener(type, listener) {
              if (type === 'message') listeners.push(listener);
            },
            postMessage(message) {
              fetch('/__ui-test/bridge', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: message
              })
                .then(response => response.text())
                .then(data => listeners.forEach(listener => listener({ data })))
                .catch(error => console.error('UI test bridge failed.', error));
            }
          };
        })();
        """;

    [Theory]
    [InlineData("HTML")]
    [InlineData("MARKDOWN")]
    public async Task SaveNewTask_WithoutMainSave_PersistsTaskEnablesControlsAndFocusesEditor(
        string bodyFormatCode)
    {
        await using var fixture = await UiAppFixture.CreateAsync();
        await fixture.SendBridgeAsync("editor.preference.save", new
        {
            bodyFormatCode,
            markdownEditType = "MARKDOWN",
            editorHeight = 360
        });
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Channel = "msedge",
            Headless = true
        });
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 1600, Height = 1000 }
        });
        await context.AddInitScriptAsync(BridgeAdapterScript);

        var page = await context.NewPageAsync();
        string? appScriptUrl = null;
        page.Request += (_, request) =>
        {
            if (new Uri(request.Url).AbsolutePath == "/js/app.js")
            {
                appScriptUrl = request.Url;
            }
        };
        const string startupVersion = "new-task-save-contract";
        await page.GotoAsync($"{fixture.BaseUrl}/index.html?v={startupVersion}", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded
        });
        await page.WaitForFunctionAsync("() => document.querySelectorAll('#task-type option').length > 0");
        Assert.Contains($"v={startupVersion}", appScriptUrl);

        var taskTitle = $"New task dialog {bodyFormatCode} browser contract";
        await page.Locator("#new-task-button").ClickAsync();
        await page.Locator("#new-task-title-input").FillAsync(taskTitle);
        await page.Locator("#new-task-save-button").ClickAsync();

        await page.Locator("#new-task-overlay").WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Hidden
        });

        Assert.Equal(taskTitle, await page.Locator("#task-title").InputValueAsync());
        Assert.Equal(bodyFormatCode, await page.Locator("#editor-mode").InputValueAsync());
        Assert.True(await IsEditorFocusedAsync(page), $"Expected the {bodyFormatCode} editor to have focus.");

        await AssertEnabledAsync(page, "#checklist-new-text");
        await AssertEnabledAsync(page, "#checklist-add-button");
        await AssertEnabledAsync(page, "#attachment-file");
        await AssertEnabledAsync(page, "#attachment-add-button");
        await AssertEnabledAsync(page, "#comment-text");
        await AssertEnabledAsync(page, "#comment-add-button");
        await AssertEnabledAsync(page, "#relationship-type");
        await AssertEnabledAsync(page, "#relationship-task");
        await AssertEnabledAsync(page, "#relationship-add-button");
        await AssertEnabledAsync(page, "#complete-button");
        await AssertEnabledAsync(page, "#cancel-button");
        await page.Locator("#timeline-list").GetByText("Task created").WaitForAsync();

        await page.Locator("#checklist-new-text").FillAsync("Confirm the saved task can be extended");
        await page.Locator("#checklist-add-button").ClickAsync();
        var checklistText = page.Locator("#checklist-list .checklist-text");
        await checklistText.WaitForAsync();
        Assert.Equal("Confirm the saved task can be extended", await checklistText.InputValueAsync());

        await page.Locator("#attachment-file").SetInputFilesAsync(new FilePayload
        {
            Name = "saved-task-proof.txt",
            MimeType = "text/plain",
            Buffer = Encoding.UTF8.GetBytes("The new task is persisted before attachments are enabled.")
        });
        await page.Locator("#attachment-add-button").ClickAsync();
        await page.Locator("#attachment-list").GetByText("saved-task-proof.txt").WaitForAsync();

        await page.Locator("#comment-text").FillAsync("The task accepts a note immediately after creation.");
        await page.Locator("#comment-add-button").ClickAsync();
        await page.Locator("#timeline-list").GetByText("The task accepts a note immediately after creation.").WaitForAsync();

        var evidence = await fixture.ReadTaskEvidenceAsync(taskTitle);
        Assert.True(evidence.TaskId > 0);
        Assert.Equal(1, evidence.ChecklistItemCount);
        Assert.Equal(1, evidence.AttachmentCount);
        Assert.Equal(1, evidence.CommentCount);
        Assert.True(evidence.LogCount >= 4);
        Assert.Contains("task.create", fixture.BridgeMessageTypes);
        Assert.Contains("task.get", fixture.BridgeMessageTypes);
        Assert.DoesNotContain("task.update", fixture.BridgeMessageTypes);
    }

    private static async Task AssertEnabledAsync(IPage page, string selector)
    {
        Assert.False(await page.Locator(selector).IsDisabledAsync(), $"Expected {selector} to be enabled.");
    }

    private static Task<bool> IsEditorFocusedAsync(IPage page)
    {
        return page.EvaluateAsync<bool>(
            """
            () => {
              const host = document.querySelector('#editor-host')
              const activeElement = document.activeElement
              return Boolean(host && activeElement && host.contains(activeElement))
            }
            """);
    }

    private sealed class UiAppFixture : IAsyncDisposable
    {
        private readonly WebApplication application;
        private readonly string testDirectory;
        private readonly string databasePath;
        private readonly ConcurrentQueue<string> bridgeMessageTypes;

        private UiAppFixture(
            WebApplication application,
            string testDirectory,
            string databasePath,
            ConcurrentQueue<string> bridgeMessageTypes,
            string baseUrl)
        {
            this.application = application;
            this.testDirectory = testDirectory;
            this.databasePath = databasePath;
            this.bridgeMessageTypes = bridgeMessageTypes;
            BaseUrl = baseUrl;
        }

        public string BaseUrl { get; }

        public IReadOnlyCollection<string> BridgeMessageTypes => bridgeMessageTypes.ToArray();

        public async Task SendBridgeAsync(string type, object payload)
        {
            var request = JsonSerializer.Serialize(new
            {
                messageId = Guid.NewGuid().ToString("N"),
                type,
                payload
            });
            using var client = new HttpClient { BaseAddress = new Uri(BaseUrl) };
            using var content = new StringContent(request, Encoding.UTF8, "application/json");
            using var response = await client.PostAsync("/__ui-test/bridge", content);
            response.EnsureSuccessStatusCode();

            using var responseDocument = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.True(
                responseDocument.RootElement.GetProperty("ok").GetBoolean(),
                responseDocument.RootElement.ToString());
        }

        public static async Task<UiAppFixture> CreateAsync()
        {
            var workspaceRoot = FindWorkspaceRoot();
            var testDirectory = Path.Combine(Path.GetTempPath(), "Okf-Todo.UiTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(testDirectory);
            var databasePath = Path.Combine(testDirectory, "okf-todo-ui-test.db");
            var bridgeMessageTypes = new ConcurrentQueue<string>();

            var builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                ContentRootPath = workspaceRoot,
                EnvironmentName = "Development"
            });
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
            builder.Services.AddDbContext<AppDbContext>(options =>
                options.UseSqlite(DatabasePathProvider.CreateConnectionString(databasePath, pooling: false)));
            builder.Services.AddSingleton<HtmlSanitizerService>();
            builder.Services.AddSingleton<IAppPreferencePathProvider>(
                new TestPreferencePathProvider(Path.Combine(testDirectory, "app-preferences.json")));
            builder.Services.AddSingleton<IBackupDestinationPicker, CancelledBackupDestinationPicker>();
            builder.Services.AddScoped<LookupSeedService>();
            builder.Services.AddScoped<TaskLifecycleService>();
            builder.Services.AddScoped<TaskService>();
            builder.Services.AddScoped<TaskAttachmentService>();
            builder.Services.AddScoped<TaskChecklistService>();
            builder.Services.AddScoped<TaskRelationService>();
            builder.Services.AddScoped<AppPreferenceService>();
            builder.Services.AddScoped<IssueService>();
            builder.Services.AddScoped<ImageService>();
            builder.Services.AddScoped<DatabaseBackupService>();
            builder.Services.AddSingleton<ApplicationCommandService>();
            builder.Services.AddSingleton<BridgeMessageHandler>();

            var application = builder.Build();
            application.UseStaticFiles(new StaticFileOptions
            {
                FileProvider = new PhysicalFileProvider(Path.Combine(workspaceRoot, "Okf-Todo", "wwwroot"))
            });
            application.MapPost("/__ui-test/bridge", async (
                HttpRequest request,
                BridgeMessageHandler handler,
                CancellationToken cancellationToken) =>
            {
                using var reader = new StreamReader(request.Body, Encoding.UTF8);
                var message = await reader.ReadToEndAsync(cancellationToken);
                using var requestDocument = JsonDocument.Parse(message);
                bridgeMessageTypes.Enqueue(requestDocument.RootElement.GetProperty("type").GetString()!);
                var response = await handler.HandleAsync(message, cancellationToken);
                return Results.Text(response, "application/json", Encoding.UTF8);
            });

            await using (var scope = application.Services.CreateAsyncScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await dbContext.Database.MigrateAsync();
                await scope.ServiceProvider.GetRequiredService<LookupSeedService>().SeedAsync();
            }

            await application.StartAsync();
            var addresses = application.Services
                .GetRequiredService<IServer>()
                .Features
                .Get<IServerAddressesFeature>()
                ?.Addresses
                ?? throw new InvalidOperationException("The UI test server did not expose an address.");
            var baseUrl = addresses.Single(address => address.StartsWith("http://127.0.0.1", StringComparison.Ordinal));

            return new UiAppFixture(application, testDirectory, databasePath, bridgeMessageTypes, baseUrl);
        }

        public async Task<TaskEvidence> ReadTaskEvidenceAsync(string title)
        {
            await using var connection = new SqliteConnection(
                $"Data Source={databasePath};Mode=ReadOnly;Pooling=False");
            await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT
                    task.Id,
                    (SELECT COUNT(*) FROM TaskChecklistItems checklist WHERE checklist.TaskId = task.Id),
                    (SELECT COUNT(*) FROM TaskAttachments attachment WHERE attachment.TaskId = task.Id),
                    (SELECT COUNT(*) FROM TaskComments comment WHERE comment.TaskId = task.Id),
                    (SELECT COUNT(*) FROM TaskLogEntries log WHERE log.TaskId = task.Id)
                FROM TaskItems task
                WHERE task.Title = $title;
                """;
            command.Parameters.AddWithValue("$title", title);

            await using var reader = await command.ExecuteReaderAsync();
            Assert.True(await reader.ReadAsync(), "The task was not persisted in the isolated SQLite database.");
            return new TaskEvidence(
                reader.GetInt32(0),
                reader.GetInt32(1),
                reader.GetInt32(2),
                reader.GetInt32(3),
                reader.GetInt32(4));
        }

        public async ValueTask DisposeAsync()
        {
            await application.StopAsync();
            await application.DisposeAsync();

            if (Directory.Exists(testDirectory))
            {
                Directory.Delete(testDirectory, recursive: true);
            }
        }

        private static string FindWorkspaceRoot()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null)
            {
                if (File.Exists(Path.Combine(directory.FullName, "Okf-Todo.slnx")))
                {
                    return directory.FullName;
                }

                directory = directory.Parent;
            }

            throw new DirectoryNotFoundException("Could not locate the OKF-Todo workspace root.");
        }
    }

    private sealed class TestPreferencePathProvider(string path) : IAppPreferencePathProvider
    {
        public string GetPreferencesPath() => path;
    }

    private sealed class CancelledBackupDestinationPicker : IBackupDestinationPicker
    {
        public Task<string?> PickAsync(
            string suggestedFileName,
            string? initialDirectory,
            CancellationToken cancellationToken) => Task.FromResult<string?>(null);
    }

    private sealed record TaskEvidence(
        int TaskId,
        int ChecklistItemCount,
        int AttachmentCount,
        int CommentCount,
        int LogCount);
}
