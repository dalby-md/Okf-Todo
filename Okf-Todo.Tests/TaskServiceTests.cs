using Microsoft.EntityFrameworkCore;
using Photino.Okf_Todo.Services;

namespace Okf_Todo.Tests;

public sealed class TaskServiceTests
{
    [Fact]
    public async Task GetLookups_ReturnsActiveLookupValuesForBasicTaskUi()
    {
        await using var database = await TestDatabase.CreateAsync();

        var lookups = await database.Tasks.GetLookupsAsync(CancellationToken.None);

        Assert.Contains(lookups.TaskTypes, item => item.Code == "ERROR" && item.Name == "Error");
        Assert.Contains(lookups.TaskPriorities, item => item.Code == "NORMAL" && item.Name == "Normal");
        Assert.Contains(lookups.TaskSources, item => item.Code == "EMAIL" && item.Name == "Email");
        Assert.Contains(lookups.BodyFormats, item => item.Code == "HTML" && item.Name == "HTML");
    }

    [Fact]
    public async Task CreateListGetAndUpdate_PersistBasicTaskFieldsAndLogsMeaningfulChanges()
    {
        await using var database = await TestDatabase.CreateAsync();
        var deadline = new DateTime(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc);

        var created = await database.Tasks.CreateAsync(new TaskSaveRequest(
            Id: null,
            Title: "  Fix failed deployment  ",
            TaskTypeCode: "ERROR",
            Body: "<p>Initial body</p>",
            BodyFormatCode: "HTML",
            TaskPriorityCode: "NORMAL",
            TaskSourceCode: "EMAIL",
            SourceReference: "  INC123456  ",
            SourceUrl: "  https://example.test/inc/123456  ",
            Deadline: deadline), CancellationToken.None);

        Assert.Equal("Fix failed deployment", created.Title);
        Assert.Equal(TaskStatusCodes.New, created.TaskStatusCode);
        Assert.Equal("ERROR", created.TaskTypeCode);
        Assert.Equal("NORMAL", created.TaskPriorityCode);
        Assert.Equal("EMAIL", created.TaskSourceCode);
        Assert.Equal("INC123456", created.SourceReference);
        Assert.Equal("https://example.test/inc/123456", created.SourceUrl);

        var activeTasks = await database.Tasks.ListAsync(new TaskListRequest("active"), CancellationToken.None);
        var listed = Assert.Single(activeTasks);
        Assert.Equal(created.Id, listed.Id);
        Assert.Equal("Fix failed deployment", listed.Title);
        Assert.Equal(TaskStatusCodes.New, listed.TaskStatusCode);

        var loaded = await database.Tasks.GetAsync(created.Id, CancellationToken.None);
        Assert.Equal("<p>Initial body</p>", loaded.Body);

        var updatedDeadline = new DateTime(2026, 7, 20, 0, 0, 0, DateTimeKind.Utc);
        var updated = await database.Tasks.UpdateAsync(new TaskSaveRequest(
            Id: created.Id,
            Title: "Investigate failed deployment",
            TaskTypeCode: "INVESTIGATION",
            Body: "<p>Updated body</p>",
            BodyFormatCode: "HTML",
            TaskPriorityCode: "URGENT",
            TaskSourceCode: "TEAMS",
            SourceReference: "Release room",
            SourceUrl: null,
            Deadline: updatedDeadline), CancellationToken.None);

        Assert.Equal("Investigate failed deployment", updated.Title);
        Assert.Equal("<p>Updated body</p>", updated.Body);
        Assert.Equal("INVESTIGATION", updated.TaskTypeCode);
        Assert.Equal("URGENT", updated.TaskPriorityCode);
        Assert.Equal("TEAMS", updated.TaskSourceCode);
        Assert.Equal("Release room", updated.SourceReference);
        Assert.Null(updated.SourceUrl);
        Assert.Equal(updatedDeadline, updated.Deadline);
        Assert.True(updated.UpdatedAt >= created.UpdatedAt);

        var savedTask = await database.LoadTaskAsync(created.Id);
        Assert.Contains(savedTask.LogEntries, log => log.TaskLogType?.Code == TaskLogTypeCodes.TaskCreated);
        Assert.Contains(savedTask.LogEntries, log => log.TaskLogType?.Code == TaskLogTypeCodes.TypeChanged);
        Assert.Contains(savedTask.LogEntries, log => log.TaskLogType?.Code == TaskLogTypeCodes.PriorityChanged);
        Assert.Contains(savedTask.LogEntries, log => log.TaskLogType?.Code == TaskLogTypeCodes.DeadlineChanged);
    }

    [Fact]
    public async Task LifecycleActionsThroughTaskService_ReturnUpdatedTaskDetails()
    {
        await using var database = await TestDatabase.CreateAsync();
        var created = await database.Tasks.CreateAsync(CreateRequest("Task to start"), CancellationToken.None);

        var started = await database.Tasks.StartAsync(created.Id, CancellationToken.None);
        Assert.Equal(TaskStatusCodes.Active, started.TaskStatusCode);

        var completed = await database.Tasks.CompleteAsync(created.Id, CancellationToken.None);
        Assert.Equal(TaskStatusCodes.Completed, completed.TaskStatusCode);

        var cancellable = await database.Tasks.CreateAsync(CreateRequest("Task to cancel"), CancellationToken.None);
        var cancelled = await database.Tasks.CancelAsync(cancellable.Id, CancellationToken.None);
        Assert.Equal(TaskStatusCodes.Cancelled, cancelled.TaskStatusCode);

        var completedTasks = await database.Tasks.ListAsync(new TaskListRequest("completed"), CancellationToken.None);
        Assert.Contains(completedTasks, task => task.Id == completed.Id && task.TaskStatusCode == TaskStatusCodes.Completed);
        Assert.Contains(completedTasks, task => task.Id == cancelled.Id && task.TaskStatusCode == TaskStatusCodes.Cancelled);
    }

    [Fact]
    public async Task CreateAndUpdate_RejectMissingRequiredFields()
    {
        await using var database = await TestDatabase.CreateAsync();

        var missingTitle = await Assert.ThrowsAsync<ValidationException>(() =>
            database.Tasks.CreateAsync(CreateRequest(" "), CancellationToken.None));
        Assert.Equal("title", missingTitle.Field);

        var created = await database.Tasks.CreateAsync(CreateRequest("Valid task"), CancellationToken.None);
        var missingType = await Assert.ThrowsAsync<ValidationException>(() =>
            database.Tasks.UpdateAsync(new TaskSaveRequest(
                created.Id,
                "Valid task",
                "",
                null,
                "HTML",
                null,
                null,
                null,
                null,
                null), CancellationToken.None));
        Assert.Equal("taskTypeCode", missingType.Field);
    }

    private static TaskSaveRequest CreateRequest(string title)
    {
        return new TaskSaveRequest(
            Id: null,
            Title: title,
            TaskTypeCode: "ERROR",
            Body: null,
            BodyFormatCode: "HTML",
            TaskPriorityCode: "NORMAL",
            TaskSourceCode: null,
            SourceReference: null,
            SourceUrl: null,
            Deadline: null);
    }
}
