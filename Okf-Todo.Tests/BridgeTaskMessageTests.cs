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
        Assert.Equal(TaskStatusCodes.New, created.GetProperty("taskStatusCode").GetString());

        var list = await fixture.SendAsync("task.list", new { view = "active" });
        Assert.Contains(list.EnumerateArray(), task => task.GetProperty("id").GetInt32() == taskId);

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

        var started = await fixture.SendAsync("task.start", new { id = taskId });
        Assert.Equal(TaskStatusCodes.Active, started.GetProperty("taskStatusCode").GetString());

        var startUndone = await fixture.SendAsync("task.undoStart", new { id = taskId });
        Assert.Equal(TaskStatusCodes.New, startUndone.GetProperty("taskStatusCode").GetString());

        started = await fixture.SendAsync("task.start", new { id = taskId });
        Assert.Equal(TaskStatusCodes.Active, started.GetProperty("taskStatusCode").GetString());

        var waiting = await fixture.SendAsync("task.waiting.add", new
        {
            taskId,
            label = "INC789"
        });
        Assert.Equal(TaskStatusCodes.Waiting, waiting.GetProperty("taskStatusCode").GetString());
        Assert.Equal("INC789", waiting.GetProperty("activeWaitingFor").GetProperty("label").GetString());

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

    private sealed class BridgeFixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        private readonly ServiceProvider services;
        private readonly BridgeMessageHandler handler;

        private BridgeFixture(SqliteConnection connection, ServiceProvider services, BridgeMessageHandler handler)
        {
            this.connection = connection;
            this.services = services;
            this.handler = handler;
        }

        public static async Task<BridgeFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();

            var serviceCollection = new ServiceCollection();
            serviceCollection.AddLogging();
            serviceCollection.AddDbContext<AppDbContext>(options => options.UseSqlite(connection));
            serviceCollection.AddScoped<LookupSeedService>();
            serviceCollection.AddScoped<TaskLifecycleService>();
            serviceCollection.AddScoped<TaskService>();
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

            return new BridgeFixture(connection, services, handler);
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
        }
    }
}
