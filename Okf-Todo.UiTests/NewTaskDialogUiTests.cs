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
        Assert.Equal(
            $"/css/app.css?v={startupVersion}",
            await page.Locator("#app-stylesheet").GetAttributeAsync("href"));
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

    [Fact]
    public async Task OwnershipFields_HaveIndependentPersistedVisibilityAndParticipateInOverviewSearch()
    {
        await using var fixture = await UiAppFixture.CreateAsync();
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
        await page.GotoAsync($"{fixture.BaseUrl}/index.html?v=ownership-fields-contract", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded
        });
        await page.WaitForFunctionAsync("() => document.querySelectorAll('#task-type option').length > 0");
        Assert.Equal(
            "/css/app.css?v=ownership-fields-contract",
            await page.Locator("#app-stylesheet").GetAttributeAsync("href"));

        const string taskTitle = "Ownership search browser contract";
        await page.Locator("#new-task-button").ClickAsync();
        await page.Locator("#new-task-title-input").FillAsync(taskTitle);
        await page.Locator("#new-task-save-button").ClickAsync();
        await page.Locator("#new-task-overlay").WaitForAsync(new LocatorWaitForOptions
        {
            State = WaitForSelectorState.Hidden
        });

        Assert.True(await page.Locator(".ownership-grid").IsHiddenAsync());
        Assert.True(await page.Locator(".owner-field").IsHiddenAsync());
        Assert.True(await page.Locator(".responsible-field").IsHiddenAsync());

        await OpenTaskDetailsPreferencesAsync(page);
        Assert.False(await page.Locator("#show-owner").IsCheckedAsync());
        Assert.False(await page.Locator("#show-responsible").IsCheckedAsync());

        await page.Locator("#show-owner").CheckAsync();
        Assert.True(await page.Locator("#show-owner").IsCheckedAsync());
        Assert.False(await page.Locator("#show-responsible").IsCheckedAsync());
        await WaitForDisplayPreferenceSavedAsync(page);
        await page.Locator("#settings-close-button").ClickAsync();

        Assert.False(await page.Locator(".ownership-grid").IsHiddenAsync());
        Assert.False(await page.Locator(".owner-field").IsHiddenAsync());
        Assert.True(await page.Locator(".responsible-field").IsHiddenAsync());

        await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync("() => document.querySelectorAll('#task-type option').length > 0");
        await page.Locator("#task-title").WaitForAsync();
        Assert.False(await page.Locator(".owner-field").IsHiddenAsync());
        Assert.True(await page.Locator(".responsible-field").IsHiddenAsync());

        await OpenTaskDetailsPreferencesAsync(page);
        Assert.True(await page.Locator("#show-owner").IsCheckedAsync());
        Assert.False(await page.Locator("#show-responsible").IsCheckedAsync());
        await page.Locator("#show-responsible").CheckAsync();
        Assert.True(await page.Locator("#show-owner").IsCheckedAsync());
        Assert.True(await page.Locator("#show-responsible").IsCheckedAsync());
        await WaitForDisplayPreferenceSavedAsync(page);
        await page.Locator("#settings-close-button").ClickAsync();

        await page.SetViewportSizeAsync(680, 1000);
        var ownerBox = await page.Locator(".owner-field").BoundingBoxAsync();
        var responsibleBox = await page.Locator(".responsible-field").BoundingBoxAsync();
        Assert.NotNull(ownerBox);
        Assert.NotNull(responsibleBox);
        Assert.InRange(Math.Abs(ownerBox.Y - responsibleBox.Y), 0, 1);
        Assert.True(ownerBox.X < responsibleBox.X);

        await page.Locator("#task-owner").FillAsync("North Support");
        await page.Locator("#task-responsible").FillAsync("Anna Jensen");
        await page.Locator("#save-button").ClickAsync();
        await page.WaitForFunctionAsync("() => document.querySelector('#save-status').textContent === 'Saved'");

        await page.Locator("#task-search").FillAsync("north support");
        await page.Locator("#task-list .task-row").GetByText(taskTitle).WaitForAsync();
        Assert.Single(await page.Locator("#task-list .task-row").AllAsync());

        await page.Locator("#task-search").FillAsync("anna jensen");
        await page.Locator("#task-list .task-row").GetByText(taskTitle).WaitForAsync();
        Assert.Single(await page.Locator("#task-list .task-row").AllAsync());

        await page.Locator("#task-search").FillAsync("");
        await OpenTaskDetailsPreferencesAsync(page);
        await page.Locator("#show-owner").UncheckAsync();
        Assert.False(await page.Locator("#show-owner").IsCheckedAsync());
        Assert.True(await page.Locator("#show-responsible").IsCheckedAsync());
        await WaitForDisplayPreferenceSavedAsync(page);
        await page.Locator("#settings-close-button").ClickAsync();
        Assert.True(await page.Locator(".owner-field").IsHiddenAsync());
        Assert.False(await page.Locator(".responsible-field").IsHiddenAsync());

        await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.DOMContentLoaded });
        await page.WaitForFunctionAsync("() => document.querySelectorAll('#task-type option').length > 0");
        Assert.True(await page.Locator(".owner-field").IsHiddenAsync());
        Assert.False(await page.Locator(".responsible-field").IsHiddenAsync());

        await OpenTaskDetailsPreferencesAsync(page);
        Assert.False(await page.Locator("#show-owner").IsCheckedAsync());
        Assert.True(await page.Locator("#show-responsible").IsCheckedAsync());
        await page.Locator("#show-responsible").UncheckAsync();
        await WaitForDisplayPreferenceSavedAsync(page);
        await page.Locator("#settings-close-button").ClickAsync();
        Assert.True(await page.Locator(".ownership-grid").IsHiddenAsync());
        Assert.True(await page.Locator(".owner-field").IsHiddenAsync());
        Assert.True(await page.Locator(".responsible-field").IsHiddenAsync());
    }

    [Fact]
    public async Task TriageCommandWorkspace_AdaptsAcrossLargeCompactAndSmallWindows()
    {
        await using var fixture = await UiAppFixture.CreateAsync(seedSampleTasks: true);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Channel = "msedge",
            Headless = true
        });
        await using var context = await browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = 1487, Height = 1058 }
        });
        await context.AddInitScriptAsync(BridgeAdapterScript);

        var page = await context.NewPageAsync();
        await page.GotoAsync($"{fixture.BaseUrl}/index.html?v=triage-command-responsive-contract", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.DOMContentLoaded
        });
        await page.WaitForFunctionAsync("() => document.querySelectorAll('#task-type option').length > 0");
        await page.Locator("#task-list .task-row").First.ClickAsync();
        await page.Locator(".tox-tinymce").WaitForAsync();

        var largeRail = await page.Locator(".task-view-rail").BoundingBoxAsync();
        var largeList = await page.Locator(".task-sidebar").BoundingBoxAsync();
        var largeEditor = await page.Locator(".task-editor-panel").BoundingBoxAsync();
        Assert.NotNull(largeRail);
        Assert.NotNull(largeList);
        Assert.NotNull(largeEditor);
        Assert.True(largeRail.Width >= 160);
        Assert.True(largeList.Width >= 360);
        Assert.True(largeRail.X < largeList.X);
        Assert.True(largeList.X < largeEditor.X);
        Assert.False(await page.Locator(".task-view-rail-label").First.IsHiddenAsync());
        Assert.True(await page.Locator(".task-view-compact").IsHiddenAsync());
        var semanticIconColors = await page.Locator(".task-view-rail-button .fluent-icon")
            .EvaluateAllAsync<string[]>("icons => icons.map(icon => getComputedStyle(icon).color)");
        Assert.True(
            semanticIconColors.Distinct(StringComparer.Ordinal).Count() >= 5,
            "Expected the task views to retain distinct semantic icon colors.");
        await AssertNoHorizontalPageOverflowAsync(page);
        await page.Locator(".task-view-rail-button[data-task-view='urgent']").ClickAsync();
        await page.WaitForFunctionAsync(
            "() => document.querySelector('#task-list-title').textContent === 'Urgent' && document.querySelector('#task-view').value === 'urgent'");
        await page.Locator(".task-view-rail-button[data-task-view='active']").ClickAsync();
        await page.WaitForFunctionAsync(
            "() => document.querySelector('#task-list-title').textContent === 'Active' && document.querySelector('#task-view').value === 'active'");
        await CaptureWorkspaceAsync(page, "triage-command-large.png");

        await page.SetViewportSizeAsync(1100, 900);
        var compactRail = await page.Locator(".task-view-rail").BoundingBoxAsync();
        Assert.NotNull(compactRail);
        Assert.InRange(compactRail.Width, 60, 76);
        Assert.True(await page.Locator(".task-view-rail-label").First.IsHiddenAsync());
        Assert.True(await page.Locator(".task-view-compact").IsHiddenAsync());
        await AssertNoHorizontalPageOverflowAsync(page);
        await CaptureWorkspaceAsync(page, "triage-command-compact.png");

        await page.SetViewportSizeAsync(820, 900);
        Assert.True(await page.Locator(".task-view-rail").IsHiddenAsync());
        Assert.False(await page.Locator(".task-view-compact").IsHiddenAsync());
        var stackedList = await page.Locator(".task-sidebar").BoundingBoxAsync();
        var stackedResizer = await page.Locator("#layout-resizer").BoundingBoxAsync();
        var stackedEditor = await page.Locator(".task-editor-panel").BoundingBoxAsync();
        Assert.NotNull(stackedList);
        Assert.NotNull(stackedResizer);
        Assert.NotNull(stackedEditor);
        Assert.True(stackedList.Y < stackedResizer.Y);
        Assert.True(stackedResizer.Y < stackedEditor.Y);
        var firstSmallTask = await page.Locator("#task-list .task-row").First.BoundingBoxAsync();
        Assert.NotNull(firstSmallTask);
        Assert.True(firstSmallTask.Y < stackedList.Y + stackedList.Height);
        await AssertNoHorizontalPageOverflowAsync(page);
        await CaptureWorkspaceAsync(page, "triage-command-small.png");

        await page.SetViewportSizeAsync(1487, 1058);
        await OpenAppearancePreferencesAsync(page);
        var layoutPreferencePanel = await page.Locator(".preferences-layout-control")
            .EvaluateAsync<string>("control => control.closest('[data-preference-panel]').dataset.preferencePanel");
        Assert.Equal("appearance", layoutPreferencePanel);
        await page.Locator(".preferences-layout-control .preference-choice[data-value='STACKED']").ClickAsync();
        await page.WaitForFunctionAsync(
            "() => document.documentElement.classList.contains('layout-mode-stacked') && document.querySelector('#save-status').textContent === 'Layout preference saved'");
        await page.Locator("#settings-close-button").ClickAsync();

        Assert.True(await page.Locator(".task-view-rail").IsHiddenAsync());
        Assert.False(await page.Locator(".task-view-compact").IsHiddenAsync());
        var explicitStackedList = await page.Locator(".task-sidebar").BoundingBoxAsync();
        var explicitStackedResizer = await page.Locator("#layout-resizer").BoundingBoxAsync();
        var explicitStackedEditor = await page.Locator(".task-editor-panel").BoundingBoxAsync();
        var explicitStackedBody = await page.Locator(".editor-host").BoundingBoxAsync();
        Assert.NotNull(explicitStackedList);
        Assert.NotNull(explicitStackedResizer);
        Assert.NotNull(explicitStackedEditor);
        Assert.NotNull(explicitStackedBody);
        Assert.True(explicitStackedList.Y < explicitStackedResizer.Y);
        Assert.True(explicitStackedResizer.Y < explicitStackedEditor.Y);
        Assert.True(
            explicitStackedBody.Y < explicitStackedEditor.Y + explicitStackedEditor.Height,
            "Expected the body editor to remain visible without first scrolling the stacked detail panel.");
        Assert.InRange(explicitStackedList.Height, 220, 455);
        await AssertNoHorizontalPageOverflowAsync(page);
        await CaptureWorkspaceAsync(page, "triage-command-stacked.png");
    }

    private static async Task OpenTaskDetailsPreferencesAsync(IPage page)
    {
        await page.Locator("#settings-button").ClickAsync();
        await page.Locator("[data-preference-section='task-details']").ClickAsync();
    }

    private static async Task OpenAppearancePreferencesAsync(IPage page)
    {
        await page.Locator("#settings-button").ClickAsync();
        await page.Locator("[data-preference-section='appearance']").ClickAsync();
    }

    private static Task WaitForDisplayPreferenceSavedAsync(IPage page)
    {
        return page.WaitForFunctionAsync(
            "() => document.querySelector('#save-status').textContent === 'Display preference saved'");
    }

    private static async Task AssertEnabledAsync(IPage page, string selector)
    {
        Assert.False(await page.Locator(selector).IsDisabledAsync(), $"Expected {selector} to be enabled.");
    }

    private static async Task AssertNoHorizontalPageOverflowAsync(IPage page)
    {
        Assert.True(await page.EvaluateAsync<bool>(
            "() => document.documentElement.scrollWidth <= document.documentElement.clientWidth"));
    }

    private static async Task CaptureWorkspaceAsync(IPage page, string fileName)
    {
        var captureDirectory = Environment.GetEnvironmentVariable("OKF_TODO_UI_CAPTURE_DIR");
        if (string.IsNullOrWhiteSpace(captureDirectory))
        {
            return;
        }

        Directory.CreateDirectory(captureDirectory);
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(captureDirectory, fileName),
            FullPage = true
        });
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

        public static async Task<UiAppFixture> CreateAsync(bool seedSampleTasks = false)
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
            builder.Services.AddScoped<SampleDataSeeder>();
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
                if (seedSampleTasks)
                {
                    await scope.ServiceProvider.GetRequiredService<SampleDataSeeder>().SeedAsync();
                }
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
