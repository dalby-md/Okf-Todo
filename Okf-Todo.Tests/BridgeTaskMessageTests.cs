using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Photino.Okf_Todo.Bridge;
using Photino.Okf_Todo.Data;
using Photino.Okf_Todo.Services;

namespace Okf_Todo.Tests;

public sealed class BridgeTaskMessageTests
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [Fact]
    public async Task Bridge_DispatchesBasicTaskMessages()
    {
        await using var fixture = await BridgeFixture.CreateAsync();

        var created = await fixture.SendAsync("task.create", new
        {
            title = "Bridge task",
            taskTypeCode = "ERROR",
            body = "<p>Created through bridge</p>",
            bodyFormatCode = "HTML",
            taskPriorityCode = "NORMAL",
            taskSourceCode = "EMAIL",
            sourceReference = "INC456",
            sourceUrl = (string?)null,
            deadline = (DateTime?)null
        });

        var taskId = created.GetProperty("id").GetInt32();
        Assert.Equal("Bridge task", created.GetProperty("title").GetString());
        Assert.Equal(TaskStatusCodes.Active, created.GetProperty("taskStatusCode").GetString());

        var activeList = await fixture.SendAsync("task.list", new { view = "active" });
        Assert.Contains(activeList.EnumerateArray(), task => task.GetProperty("id").GetInt32() == taskId);

        var lookupSettings = await fixture.SendAsync("lookup.settings.get", new { });
        Assert.Contains(
            lookupSettings.GetProperty("taskTypes").EnumerateArray(),
            item => item.GetProperty("code").GetString() == "ERROR");

        var updatedLookupSettings = await fixture.SendAsync("lookup.settings.update", new
        {
            group = "taskPriorities",
            code = "NORMAL",
            name = "Standard",
            description = "Normal priority",
            sortOrder = 22,
            isActive = true,
            isSelected = true,
            backgroundColor = "#112233",
            foregroundColor = "#ddeeff"
        });
        Assert.Contains(
            updatedLookupSettings.GetProperty("taskPriorities").EnumerateArray(),
            item => item.GetProperty("code").GetString() == "NORMAL"
                && item.GetProperty("name").GetString() == "Standard"
                && item.GetProperty("backgroundColor").GetString() == "#112233");

        var createdLookupSettings = await fixture.SendAsync("lookup.settings.create", new
        {
            group = "taskTypes",
            code = "QUESTION",
            name = "Question",
            description = "Needs an answer",
            sortOrder = 45,
            isActive = true,
            isSelected = false,
            backgroundColor = "#e0f2fe",
            foregroundColor = "#0f172a"
        });
        Assert.Contains(
            createdLookupSettings.GetProperty("taskTypes").EnumerateArray(),
            item => item.GetProperty("code").GetString() == "QUESTION"
                && item.GetProperty("name").GetString() == "Question"
                && item.GetProperty("foregroundColor").GetString() == "#0f172a"
                && item.GetProperty("canDelete").GetBoolean());

        var deletedLookupSettings = await fixture.SendAsync("lookup.settings.delete", new
        {
            group = "taskTypes",
            code = "QUESTION"
        });
        Assert.DoesNotContain(
            deletedLookupSettings.GetProperty("taskTypes").EnumerateArray(),
            item => item.GetProperty("code").GetString() == "QUESTION");

        var loaded = await fixture.SendAsync("task.get", new { id = taskId });
        Assert.Equal("<p>Created through bridge</p>", loaded.GetProperty("body").GetString());

        var updated = await fixture.SendAsync("task.update", new
        {
            id = taskId,
            title = "Updated bridge task",
            taskTypeCode = "REQUEST",
            body = "<p>Updated through bridge</p>",
            bodyFormatCode = "HTML",
            taskPriorityCode = "URGENT",
            taskSourceCode = "TEAMS",
            sourceReference = "Chat",
            sourceUrl = (string?)null,
            deadline = (DateTime?)null
        });
        Assert.Equal("Updated bridge task", updated.GetProperty("title").GetString());
        Assert.Equal("REQUEST", updated.GetProperty("taskTypeCode").GetString());

        var saveDrivenWaiting = await fixture.SendAsync("task.update", new
        {
            id = taskId,
            title = "Updated bridge task",
            taskTypeCode = "REQUEST",
            body = "<p>Updated through bridge</p>",
            bodyFormatCode = "HTML",
            taskPriorityCode = "URGENT",
            taskSourceCode = "TEAMS",
            sourceReference = "Chat",
            sourceUrl = (string?)null,
            deadline = (DateTime?)null,
            activeWaitingForLabel = "SAVEWAIT"
        });
        Assert.Equal("SAVEWAIT", saveDrivenWaiting.GetProperty("activeWaitingFor").GetProperty("label").GetString());

        var saveDrivenWaitingList = await fixture.SendAsync("task.list", new { view = "active" });
        var saveDrivenWaitingListItem = Assert.Single(
            saveDrivenWaitingList.EnumerateArray(),
            task => task.GetProperty("id").GetInt32() == taskId);
        Assert.Equal("SAVEWAIT", saveDrivenWaitingListItem.GetProperty("activeWaitingForLabel").GetString());

        var saveDrivenWaitingCleared = await fixture.SendAsync("task.update", new
        {
            id = taskId,
            title = "Updated bridge task",
            taskTypeCode = "REQUEST",
            body = "<p>Updated through bridge</p>",
            bodyFormatCode = "HTML",
            taskPriorityCode = "URGENT",
            taskSourceCode = "TEAMS",
            sourceReference = "Chat",
            sourceUrl = (string?)null,
            deadline = (DateTime?)null,
            activeWaitingForLabel = (string?)null
        });
        Assert.Equal(JsonValueKind.Null, saveDrivenWaitingCleared.GetProperty("activeWaitingFor").ValueKind);

        var started = await fixture.SendAsync("task.start", new { id = taskId });
        Assert.Equal(TaskStatusCodes.Active, started.GetProperty("taskStatusCode").GetString());

        var waiting = await fixture.SendAsync("task.waiting.add", new
        {
            taskId,
            label = "INC789"
        });
        Assert.Equal(TaskStatusCodes.Active, waiting.GetProperty("taskStatusCode").GetString());
        Assert.Equal("INC789", waiting.GetProperty("activeWaitingFor").GetProperty("label").GetString());

        var updatedWaiting = await fixture.SendAsync("task.waiting.add", new
        {
            taskId,
            label = "Anna"
        });
        Assert.Equal(TaskStatusCodes.Active, updatedWaiting.GetProperty("taskStatusCode").GetString());
        Assert.Equal("Anna", updatedWaiting.GetProperty("activeWaitingFor").GetProperty("label").GetString());

        activeList = await fixture.SendAsync("task.list", new { view = "active" });
        var activeWaitingListItem = Assert.Single(
            activeList.EnumerateArray(),
            task => task.GetProperty("id").GetInt32() == taskId);
        Assert.Equal("Anna", activeWaitingListItem.GetProperty("activeWaitingForLabel").GetString());

        var cleared = await fixture.SendAsync("task.waiting.clear", new { id = taskId });
        Assert.Equal(TaskStatusCodes.Active, cleared.GetProperty("taskStatusCode").GetString());
        Assert.Equal(JsonValueKind.Null, cleared.GetProperty("activeWaitingFor").ValueKind);

        var completed = await fixture.SendAsync("task.complete", new { id = taskId });
        Assert.Equal(TaskStatusCodes.Completed, completed.GetProperty("taskStatusCode").GetString());

        var reopened = await fixture.SendAsync("task.reopen", new { id = taskId });
        Assert.Equal(TaskStatusCodes.Active, reopened.GetProperty("taskStatusCode").GetString());

        var cancellable = await fixture.SendAsync("task.create", new
        {
            title = "Cancellable bridge task",
            taskTypeCode = "NOTE",
            body = "",
            bodyFormatCode = "HTML",
            taskPriorityCode = (string?)null,
            taskSourceCode = (string?)null,
            sourceReference = (string?)null,
            sourceUrl = (string?)null,
            deadline = (DateTime?)null
        });

        var cancelled = await fixture.SendAsync("task.cancel", new { id = cancellable.GetProperty("id").GetInt32() });
        Assert.Equal(TaskStatusCodes.Cancelled, cancelled.GetProperty("taskStatusCode").GetString());
    }

    [Fact]
    public async Task Bridge_ReturnsValidationErrorForInvalidTaskPayload()
    {
        await using var fixture = await BridgeFixture.CreateAsync();

        var response = await fixture.SendRawAsync("task.create", new
        {
            title = "",
            taskTypeCode = "ERROR",
            body = "",
            bodyFormatCode = "HTML",
            taskPriorityCode = (string?)null,
            taskSourceCode = (string?)null,
            sourceReference = (string?)null,
            sourceUrl = (string?)null,
            deadline = (DateTime?)null
        });

        Assert.False(response.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("ValidationFailed", response.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("title", response.RootElement.GetProperty("error").GetProperty("details").GetProperty("field").GetString());
    }

    [Fact]
    public async Task Bridge_DispatchesTaskEditorImageMessages()
    {
        await using var fixture = await BridgeFixture.CreateAsync();
        var task = await fixture.SendAsync("task.create", new
        {
            title = "Bridge image task",
            taskTypeCode = "ERROR",
            body = "",
            bodyFormatCode = "HTML",
            taskPriorityCode = (string?)null,
            taskSourceCode = (string?)null,
            sourceReference = (string?)null,
            sourceUrl = (string?)null,
            deadline = (DateTime?)null
        });

        var imageBytes = new byte[] { 1, 2, 3, 4 };
        var image = await fixture.SendAsync("image.create", new
        {
            issueId = (int?)null,
            taskId = task.GetProperty("id").GetInt32(),
            filename = "paste.png",
            mimeType = "image/png",
            base64Data = Convert.ToBase64String(imageBytes),
            width = (int?)null,
            height = (int?)null
        });

        Assert.StartsWith("app://image/", image.GetProperty("src").GetString());

        var loaded = await fixture.SendAsync("image.get", new
        {
            id = image.GetProperty("id").GetInt32()
        });

        Assert.Equal("image/png", loaded.GetProperty("mimeType").GetString());
        Assert.Equal("paste.png", loaded.GetProperty("filename").GetString());
        Assert.Equal(Convert.ToBase64String(imageBytes), loaded.GetProperty("base64Data").GetString());
    }

    [Fact]
    public async Task Bridge_PersistsEditorPreference()
    {
        await using var fixture = await BridgeFixture.CreateAsync();

        var initial = await fixture.SendAsync("editor.preference.get", new { });
        Assert.Equal("HTML", initial.GetProperty("bodyFormatCode").GetString());

        var saved = await fixture.SendAsync("editor.preference.save", new
        {
            bodyFormatCode = "MARKDOWN"
        });
        Assert.Equal("MARKDOWN", saved.GetProperty("bodyFormatCode").GetString());

        var loaded = await fixture.SendAsync("editor.preference.get", new { });
        Assert.Equal("MARKDOWN", loaded.GetProperty("bodyFormatCode").GetString());
    }

    [Fact]
    public async Task Bridge_PersistsLayoutPreference()
    {
        await using var fixture = await BridgeFixture.CreateAsync();

        var initial = await fixture.SendAsync("layout.preference.get", new { });
        Assert.Equal(JsonValueKind.Null, initial.GetProperty("taskListWidth").ValueKind);
        Assert.Equal(JsonValueKind.Null, initial.GetProperty("taskListHeight").ValueKind);
        Assert.Equal("AUTO", initial.GetProperty("layoutMode").GetString());

        await fixture.SendAsync("editor.preference.save", new
        {
            bodyFormatCode = "MARKDOWN"
        });

        var saved = await fixture.SendAsync("layout.preference.save", new
        {
            taskListWidth = 412,
            taskListHeight = 275,
            layoutMode = "STACKED"
        });
        Assert.Equal(412, saved.GetProperty("taskListWidth").GetDouble());
        Assert.Equal(275, saved.GetProperty("taskListHeight").GetDouble());
        Assert.Equal("STACKED", saved.GetProperty("layoutMode").GetString());

        var loaded = await fixture.SendAsync("layout.preference.get", new { });
        Assert.Equal(412, loaded.GetProperty("taskListWidth").GetDouble());
        Assert.Equal(275, loaded.GetProperty("taskListHeight").GetDouble());
        Assert.Equal("STACKED", loaded.GetProperty("layoutMode").GetString());

        var editorPreference = await fixture.SendAsync("editor.preference.get", new { });
        Assert.Equal("MARKDOWN", editorPreference.GetProperty("bodyFormatCode").GetString());
    }

    [Fact]
    public async Task Bridge_RejectsInvalidEditorPreference()
    {
        await using var fixture = await BridgeFixture.CreateAsync();

        using var response = await fixture.SendRawAsync("editor.preference.save", new
        {
            bodyFormatCode = "PLAIN_TEXT"
        });

        Assert.False(response.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("ValidationFailed", response.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("bodyFormatCode", response.RootElement.GetProperty("error").GetProperty("details").GetProperty("field").GetString());
    }

    [Fact]
    public async Task Bridge_RejectsInvalidLayoutPreference()
    {
        await using var fixture = await BridgeFixture.CreateAsync();

        using var response = await fixture.SendRawAsync("layout.preference.save", new
        {
            taskListWidth = 40,
            taskListHeight = 275
        });

        Assert.False(response.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("ValidationFailed", response.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("taskListWidth", response.RootElement.GetProperty("error").GetProperty("details").GetProperty("field").GetString());
    }

    [Fact]
    public async Task Bridge_RejectsInvalidLayoutModePreference()
    {
        await using var fixture = await BridgeFixture.CreateAsync();

        using var response = await fixture.SendRawAsync("layout.preference.save", new
        {
            taskListWidth = 320,
            taskListHeight = 275,
            layoutMode = "FLOATING"
        });

        Assert.False(response.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("ValidationFailed", response.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("layoutMode", response.RootElement.GetProperty("error").GetProperty("details").GetProperty("field").GetString());
    }

    private sealed class BridgeFixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        private readonly string preferencesDirectory;
        private readonly ServiceProvider services;
        private readonly BridgeMessageHandler handler;

        private BridgeFixture(
            SqliteConnection connection,
            string preferencesDirectory,
            ServiceProvider services,
            BridgeMessageHandler handler)
        {
            this.connection = connection;
            this.preferencesDirectory = preferencesDirectory;
            this.services = services;
            this.handler = handler;
        }

        public static async Task<BridgeFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var preferencesDirectory = Path.Combine(Path.GetTempPath(), "Okf-Todo.Tests", Guid.NewGuid().ToString("N"));

            var serviceCollection = new ServiceCollection();
            serviceCollection.AddLogging();
            serviceCollection.AddDbContext<AppDbContext>(options => options.UseSqlite(connection));
            serviceCollection.AddSingleton<IAppPreferencePathProvider>(
                new TestAppPreferencePathProvider(Path.Combine(preferencesDirectory, "app-preferences.json")));
            serviceCollection.AddScoped<LookupSeedService>();
            serviceCollection.AddScoped<TaskLifecycleService>();
            serviceCollection.AddScoped<TaskService>();
            serviceCollection.AddScoped<AppPreferenceService>();
            serviceCollection.AddScoped<ImageService>();

            var services = serviceCollection.BuildServiceProvider();

            await using (var scope = services.CreateAsyncScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await dbContext.Database.EnsureCreatedAsync();
                await scope.ServiceProvider.GetRequiredService<LookupSeedService>().SeedAsync();
            }

            var handler = new BridgeMessageHandler(
                services,
                services.GetRequiredService<ILogger<BridgeMessageHandler>>());

            return new BridgeFixture(connection, preferencesDirectory, services, handler);
        }

        public async Task<JsonElement> SendAsync(string type, object payload)
        {
            using var response = await SendRawAsync(type, payload);
            Assert.True(response.RootElement.GetProperty("ok").GetBoolean(), response.RootElement.ToString());
            return response.RootElement.GetProperty("payload").Clone();
        }

        public async Task<JsonDocument> SendRawAsync(string type, object payload)
        {
            var request = JsonSerializer.Serialize(new
            {
                messageId = Guid.NewGuid().ToString("N"),
                type,
                payload
            }, JsonOptions);

            var response = await handler.HandleAsync(request);
            return JsonDocument.Parse(response);
        }

        public async ValueTask DisposeAsync()
        {
            await services.DisposeAsync();
            await connection.DisposeAsync();

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
