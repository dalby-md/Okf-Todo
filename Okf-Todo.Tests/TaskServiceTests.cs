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
        Assert.Contains(lookups.TaskTypes, item =>
            item.Code == "ERROR" && item.BackgroundColor == "#facc15" && item.ForegroundColor == "#111827");
        Assert.Contains(lookups.TaskTypes, item => item.Code == "REQUEST" && item.IsSelected);
        Assert.Contains(lookups.TaskPriorities, item => item.Code == "NORMAL" && item.Name == "Normal");
        Assert.Contains(lookups.TaskPriorities, item => item.Code == "NORMAL" && item.IsSelected);
        Assert.Contains(lookups.TaskPriorities, item =>
            item.Code == "URGENT" && item.BackgroundColor == "#b42318" && item.ForegroundColor == "#ffffff");
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
        Assert.Equal(TaskStatusCodes.Active, created.TaskStatusCode);
        Assert.Equal("ERROR", created.TaskTypeCode);
        Assert.Equal("NORMAL", created.TaskPriorityCode);
        Assert.Equal("EMAIL", created.TaskSourceCode);
        Assert.Equal("INC123456", created.SourceReference);
        Assert.Equal("https://example.test/inc/123456", created.SourceUrl);

        var activeTasks = await database.Tasks.ListAsync(new TaskListRequest("active"), CancellationToken.None);
        var listed = Assert.Single(activeTasks);
        Assert.Equal(created.Id, listed.Id);
        Assert.Equal("Fix failed deployment", listed.Title);
        Assert.Equal(TaskStatusCodes.Active, listed.TaskStatusCode);
        Assert.Equal("#facc15", listed.TaskTypeBackgroundColor);
        Assert.Equal("#111827", listed.TaskTypeForegroundColor);
        Assert.Equal("#6b7280", listed.TaskStatusBackgroundColor);
        Assert.Equal("#ffffff", listed.TaskStatusForegroundColor);

        var completedTasks = await database.Tasks.ListAsync(new TaskListRequest("completed"), CancellationToken.None);
        Assert.DoesNotContain(completedTasks, task => task.Id == created.Id);

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
    public async Task Create_UsesSelectedLookupDefaultsWhenTypeAndPriorityAreOmitted()
    {
        await using var database = await TestDatabase.CreateAsync();

        var created = await database.Tasks.CreateAsync(new TaskSaveRequest(
            Id: null,
            Title: "Created with defaults",
            TaskTypeCode: "",
            Body: null,
            BodyFormatCode: "HTML",
            TaskPriorityCode: null,
            TaskSourceCode: null,
            SourceReference: null,
            SourceUrl: null,
            Deadline: null), CancellationToken.None);

        Assert.Equal("REQUEST", created.TaskTypeCode);
        Assert.Equal("NORMAL", created.TaskPriorityCode);
    }

    [Fact]
    public async Task Timeline_AddsCommentsBesideAutomaticLogsAndDeletesOnlyComments()
    {
        await using var database = await TestDatabase.CreateAsync();
        var created = await database.Tasks.CreateAsync(CreateRequest("Task with comments"), CancellationToken.None);

        var initialTimeline = await database.Tasks.GetTimelineAsync(
            new TaskTimelineRequest(created.Id),
            CancellationToken.None);
        Assert.Contains(initialTimeline, item =>
            item.Kind == "log" && item.LogTypeCode == TaskLogTypeCodes.TaskCreated && !item.CanDelete);

        var timelineWithComment = await database.Tasks.AddCommentAsync(new TaskCommentCreateRequest(
            created.Id,
            "  Checked logs and found the failing deployment step.  "), CancellationToken.None);

        var comment = Assert.Single(timelineWithComment, item => item.Kind == "comment");
        Assert.Equal("Checked logs and found the failing deployment step.", comment.Text);
        Assert.True(comment.CanDelete);
        Assert.Contains(timelineWithComment, item =>
            item.Kind == "log" && item.LogTypeCode == TaskLogTypeCodes.CommentAdded && item.Text == "Comment added");

        var timelineAfterDelete = await database.Tasks.DeleteCommentAsync(new TaskCommentDeleteRequest(
            created.Id,
            comment.Id), CancellationToken.None);

        Assert.DoesNotContain(timelineAfterDelete, item => item.Kind == "comment");
        Assert.Contains(timelineAfterDelete, item =>
            item.Kind == "log" && item.LogTypeCode == TaskLogTypeCodes.CommentAdded);
    }

    [Fact]
    public async Task LookupSettings_UpdateEditableFieldsAndSelectedDefaults()
    {
        await using var database = await TestDatabase.CreateAsync();

        var settings = await database.Tasks.GetLookupSettingsAsync(CancellationToken.None);
        Assert.Contains(settings.TaskStatuses, status => status.Code == TaskStatusCodes.Active);

        var updatedSettings = await database.Tasks.UpdateLookupAsync(new LookupUpdateRequest(
            Group: "taskTypes",
            Code: "ERROR",
            Name: "Bug",
            Description: "Work caused by a product error",
            SortOrder: 15,
            IsActive: true,
            IsSelected: true,
            BackgroundColor: "#123456",
            ForegroundColor: "#abcdef"), CancellationToken.None);

        var updatedType = Assert.Single(updatedSettings.TaskTypes, type => type.Code == "ERROR");
        Assert.Equal("Bug", updatedType.Name);
        Assert.Equal("Work caused by a product error", updatedType.Description);
        Assert.Equal(15, updatedType.SortOrder);
        Assert.True(updatedType.IsSelected);
        Assert.Equal("#123456", updatedType.BackgroundColor);
        Assert.Equal("#abcdef", updatedType.ForegroundColor);
        Assert.DoesNotContain(updatedSettings.TaskTypes, type => type.Code == "REQUEST" && type.IsSelected);

        var created = await database.Tasks.CreateAsync(new TaskSaveRequest(
            Id: null,
            Title: "Uses edited default",
            TaskTypeCode: "",
            Body: null,
            BodyFormatCode: "HTML",
            TaskPriorityCode: null,
            TaskSourceCode: null,
            SourceReference: null,
            SourceUrl: null,
            Deadline: null), CancellationToken.None);
        Assert.Equal("ERROR", created.TaskTypeCode);

        var activeTasks = await database.Tasks.ListAsync(new TaskListRequest("active"), CancellationToken.None);
        var listed = Assert.Single(activeTasks, task => task.Id == created.Id);
        Assert.Equal("Bug", listed.TaskTypeName);
        Assert.Equal("#123456", listed.TaskTypeBackgroundColor);
        Assert.Equal("#abcdef", listed.TaskTypeForegroundColor);
    }

    [Fact]
    public async Task LookupSettings_CreateInsertsLookupAndCanBecomeSelectedDefault()
    {
        await using var database = await TestDatabase.CreateAsync();

        var settings = await database.Tasks.CreateLookupAsync(new LookupCreateRequest(
            Group: "taskPriorities",
            Code: "LOW_RISK",
            Name: "Low risk",
            Description: "Can wait",
            SortOrder: 5,
            IsActive: true,
            IsSelected: true,
            BackgroundColor: "#d1fae5",
            ForegroundColor: "#064e3b"), CancellationToken.None);

        var createdPriority = Assert.Single(settings.TaskPriorities, priority => priority.Code == "LOW_RISK");
        Assert.Equal("Low risk", createdPriority.Name);
        Assert.Equal("Can wait", createdPriority.Description);
        Assert.Equal(5, createdPriority.SortOrder);
        Assert.True(createdPriority.IsActive);
        Assert.True(createdPriority.IsSelected);
        Assert.False(createdPriority.IsSystem);
        Assert.True(createdPriority.CanDelete);
        Assert.Equal("#d1fae5", createdPriority.BackgroundColor);
        Assert.Equal("#064e3b", createdPriority.ForegroundColor);
        Assert.DoesNotContain(settings.TaskPriorities, priority => priority.Code == "NORMAL" && priority.IsSelected);

        var createdTask = await database.Tasks.CreateAsync(new TaskSaveRequest(
            Id: null,
            Title: "Uses created default",
            TaskTypeCode: "",
            Body: null,
            BodyFormatCode: "HTML",
            TaskPriorityCode: null,
            TaskSourceCode: null,
            SourceReference: null,
            SourceUrl: null,
            Deadline: null), CancellationToken.None);

        Assert.Equal("LOW_RISK", createdTask.TaskPriorityCode);

        settings = await database.Tasks.GetLookupSettingsAsync(CancellationToken.None);
        createdPriority = Assert.Single(settings.TaskPriorities, priority => priority.Code == "LOW_RISK");
        Assert.False(createdPriority.CanDelete);
    }

    [Fact]
    public async Task LookupSettings_CreateRejectsDuplicateCode()
    {
        await using var database = await TestDatabase.CreateAsync();

        var exception = await Assert.ThrowsAsync<ValidationException>(() =>
            database.Tasks.CreateLookupAsync(new LookupCreateRequest(
                Group: "taskTypes",
                Code: "ERROR",
                Name: "Duplicate",
                Description: null,
                SortOrder: 99,
                IsActive: true,
                IsSelected: false,
                BackgroundColor: "#ffffff",
                ForegroundColor: "#111827"), CancellationToken.None));

        Assert.Equal("code", exception.Field);
    }

    [Fact]
    public async Task LookupSettings_DeleteRemovesUnusedCustomLookup()
    {
        await using var database = await TestDatabase.CreateAsync();

        await database.Tasks.CreateLookupAsync(new LookupCreateRequest(
            Group: "taskTypes",
            Code: "QUESTION",
            Name: "Question",
            Description: null,
            SortOrder: 90,
            IsActive: true,
            IsSelected: false,
            BackgroundColor: "#e0f2fe",
            ForegroundColor: "#0f172a"), CancellationToken.None);

        var settings = await database.Tasks.DeleteLookupAsync(new LookupDeleteRequest(
            Group: "taskTypes",
            Code: "QUESTION"), CancellationToken.None);

        Assert.DoesNotContain(settings.TaskTypes, type => type.Code == "QUESTION");
    }

    [Fact]
    public async Task LookupSettings_DeleteRejectsSystemOrUsedLookup()
    {
        await using var database = await TestDatabase.CreateAsync();

        var systemException = await Assert.ThrowsAsync<ValidationException>(() =>
            database.Tasks.DeleteLookupAsync(new LookupDeleteRequest(
                Group: "taskStatuses",
                Code: TaskStatusCodes.Active), CancellationToken.None));
        Assert.Equal("code", systemException.Field);

        await database.Tasks.CreateLookupAsync(new LookupCreateRequest(
            Group: "taskPriorities",
            Code: "LOW_RISK",
            Name: "Low risk",
            Description: null,
            SortOrder: 90,
            IsActive: true,
            IsSelected: false,
            BackgroundColor: "#d1fae5",
            ForegroundColor: "#064e3b"), CancellationToken.None);
        await database.Tasks.CreateAsync(new TaskSaveRequest(
            Id: null,
            Title: "Uses low risk",
            TaskTypeCode: "ERROR",
            Body: null,
            BodyFormatCode: "HTML",
            TaskPriorityCode: "LOW_RISK",
            TaskSourceCode: null,
            SourceReference: null,
            SourceUrl: null,
            Deadline: null), CancellationToken.None);

        var usedException = await Assert.ThrowsAsync<ValidationException>(() =>
            database.Tasks.DeleteLookupAsync(new LookupDeleteRequest(
                Group: "taskPriorities",
                Code: "LOW_RISK"), CancellationToken.None));
        Assert.Equal("code", usedException.Field);
    }

    [Fact]
    public async Task LookupSettings_ReorderAssignsNormalizedSortOrder()
    {
        await using var database = await TestDatabase.CreateAsync();

        var settings = await database.Tasks.GetLookupSettingsAsync(CancellationToken.None);
        var orderedCodes = settings.TaskTypes
            .Select(type => type.Code)
            .Reverse()
            .ToArray();

        var reordered = await database.Tasks.ReorderLookupAsync(new LookupReorderRequest(
            Group: "taskTypes",
            OrderedCodes: orderedCodes), CancellationToken.None);
        var reorderedTypes = reordered.TaskTypes.ToArray();

        Assert.Equal(orderedCodes, reorderedTypes.Select(type => type.Code));
        Assert.Equal(10, reorderedTypes[0].SortOrder);
        Assert.Equal(20, reorderedTypes[1].SortOrder);
        Assert.Equal(30, reorderedTypes[2].SortOrder);

        var lookups = await database.Tasks.GetLookupsAsync(CancellationToken.None);
        Assert.Equal(orderedCodes, lookups.TaskTypes.Select(type => type.Code));
    }

    [Fact]
    public async Task LookupSettings_ReorderRejectsMissingOrDuplicateCodes()
    {
        await using var database = await TestDatabase.CreateAsync();

        var duplicateException = await Assert.ThrowsAsync<ValidationException>(() =>
            database.Tasks.ReorderLookupAsync(new LookupReorderRequest(
                Group: "taskTypes",
                OrderedCodes: ["ERROR", "ERROR"]), CancellationToken.None));
        Assert.Equal("orderedCodes", duplicateException.Field);

        var missingException = await Assert.ThrowsAsync<ValidationException>(() =>
            database.Tasks.ReorderLookupAsync(new LookupReorderRequest(
                Group: "taskTypes",
                OrderedCodes: ["ERROR"]), CancellationToken.None));
        Assert.Equal("orderedCodes", missingException.Field);
    }

    [Fact]
    public async Task LookupSettings_PreventRequiredStatusDeactivation()
    {
        await using var database = await TestDatabase.CreateAsync();

        var exception = await Assert.ThrowsAsync<ValidationException>(() =>
            database.Tasks.UpdateLookupAsync(new LookupUpdateRequest(
                Group: "taskStatuses",
                Code: TaskStatusCodes.Active,
                Name: "Active",
                Description: null,
                SortOrder: 10,
                IsActive: false,
                IsSelected: false,
                BackgroundColor: "#6b7280",
                ForegroundColor: "#ffffff"), CancellationToken.None));

        Assert.Equal("isActive", exception.Field);
    }

    [Fact]
    public async Task LookupSettings_RejectInvalidColor()
    {
        await using var database = await TestDatabase.CreateAsync();

        var exception = await Assert.ThrowsAsync<ValidationException>(() =>
            database.Tasks.UpdateLookupAsync(new LookupUpdateRequest(
                Group: "taskPriorities",
                Code: "NORMAL",
                Name: "Normal",
                Description: null,
                SortOrder: 20,
                IsActive: true,
                IsSelected: true,
                BackgroundColor: "red",
                ForegroundColor: "#ffffff"), CancellationToken.None));

        Assert.Equal("BackgroundColor", exception.Field);
    }

    [Fact]
    public async Task LifecycleActionsThroughTaskService_ReturnUpdatedTaskDetails()
    {
        await using var database = await TestDatabase.CreateAsync();
        var created = await database.Tasks.CreateAsync(CreateRequest("Task to start"), CancellationToken.None);

        var started = await database.Tasks.StartAsync(created.Id, CancellationToken.None);
        Assert.Equal(TaskStatusCodes.Active, started.TaskStatusCode);

        var undoStartException = await Assert.ThrowsAsync<ValidationException>(() =>
            database.Tasks.UndoStartAsync(created.Id, CancellationToken.None));
        Assert.Equal("taskStatus", undoStartException.Field);

        var completed = await database.Tasks.CompleteAsync(created.Id, CancellationToken.None);
        Assert.Equal(TaskStatusCodes.Completed, completed.TaskStatusCode);

        var reopened = await database.Tasks.ReopenAsync(created.Id, CancellationToken.None);
        Assert.Equal(TaskStatusCodes.Active, reopened.TaskStatusCode);

        completed = await database.Tasks.CompleteAsync(created.Id, CancellationToken.None);
        Assert.Equal(TaskStatusCodes.Completed, completed.TaskStatusCode);

        var cancellable = await database.Tasks.CreateAsync(CreateRequest("Task to cancel"), CancellationToken.None);
        var cancelled = await database.Tasks.CancelAsync(cancellable.Id, CancellationToken.None);
        Assert.Equal(TaskStatusCodes.Cancelled, cancelled.TaskStatusCode);

        var completedTasks = await database.Tasks.ListAsync(new TaskListRequest("completed"), CancellationToken.None);
        Assert.Contains(completedTasks, task => task.Id == completed.Id && task.TaskStatusCode == TaskStatusCodes.Completed);
        Assert.Contains(completedTasks, task => task.Id == cancelled.Id && task.TaskStatusCode == TaskStatusCodes.Cancelled);
    }

    [Fact]
    public async Task AddAndClearWaitingFor_UsesLifecycleRulesAndReturnsActiveWaitingTarget()
    {
        await using var database = await TestDatabase.CreateAsync();
        var created = await database.Tasks.CreateAsync(CreateRequest("Task waiting on ServiceDesk"), CancellationToken.None);

        var waiting = await database.Tasks.AddWaitingForAsync(new TaskWaitingForSaveRequest(
            TaskId: created.Id,
            Label: "INC123456"), CancellationToken.None);

        Assert.Equal(TaskStatusCodes.Active, waiting.TaskStatusCode);
        Assert.NotNull(waiting.ActiveWaitingFor);
        Assert.Equal("INC123456", waiting.ActiveWaitingFor.Label);

        var activeTasks = await database.Tasks.ListAsync(new TaskListRequest("active"), CancellationToken.None);
        Assert.Contains(activeTasks, task => task.Id == created.Id
            && task.TaskStatusCode == TaskStatusCodes.Active
            && task.ActiveWaitingForLabel == "INC123456");

        var updatedWaiting = await database.Tasks.AddWaitingForAsync(new TaskWaitingForSaveRequest(
            created.Id,
            "Anna"), CancellationToken.None);
        Assert.Equal(TaskStatusCodes.Active, updatedWaiting.TaskStatusCode);
        Assert.NotNull(updatedWaiting.ActiveWaitingFor);
        Assert.Equal("Anna", updatedWaiting.ActiveWaitingFor.Label);

        var cleared = await database.Tasks.ClearWaitingForAsync(created.Id, CancellationToken.None);
        Assert.Equal(TaskStatusCodes.Active, cleared.TaskStatusCode);
        Assert.Null(cleared.ActiveWaitingFor);

        var savedTask = await database.LoadTaskAsync(created.Id);
        Assert.Contains(savedTask.LogEntries, log => log.TaskLogType?.Code == TaskLogTypeCodes.WaitingForChanged);
        Assert.Contains(savedTask.LogEntries, log => log.TaskLogType?.Code == TaskLogTypeCodes.WaitingForCleared);
    }

    [Fact]
    public async Task Update_CanSetAndClearWaitingForThroughNormalSave()
    {
        await using var database = await TestDatabase.CreateAsync();
        var created = await database.Tasks.CreateAsync(CreateRequest("Task with saved waiting field"), CancellationToken.None);

        var waiting = await database.Tasks.UpdateAsync(new TaskSaveRequest(
            Id: created.Id,
            Title: created.Title,
            TaskTypeCode: created.TaskTypeCode,
            Body: created.Body,
            BodyFormatCode: "HTML",
            TaskPriorityCode: created.TaskPriorityCode,
            TaskSourceCode: null,
            SourceReference: null,
            SourceUrl: null,
            Deadline: null,
            ActiveWaitingForLabel: "INC123456"), CancellationToken.None);

        Assert.Equal(TaskStatusCodes.Active, waiting.TaskStatusCode);
        Assert.NotNull(waiting.ActiveWaitingFor);
        Assert.Equal("INC123456", waiting.ActiveWaitingFor.Label);

        var activeTasks = await database.Tasks.ListAsync(new TaskListRequest("active"), CancellationToken.None);
        Assert.Contains(activeTasks, task => task.Id == created.Id && task.ActiveWaitingForLabel == "INC123456");

        var cleared = await database.Tasks.UpdateAsync(new TaskSaveRequest(
            Id: created.Id,
            Title: created.Title,
            TaskTypeCode: created.TaskTypeCode,
            Body: created.Body,
            BodyFormatCode: "HTML",
            TaskPriorityCode: created.TaskPriorityCode,
            TaskSourceCode: null,
            SourceReference: null,
            SourceUrl: null,
            Deadline: null,
            ActiveWaitingForLabel: null), CancellationToken.None);

        Assert.Equal(TaskStatusCodes.Active, cleared.TaskStatusCode);
        Assert.Null(cleared.ActiveWaitingFor);
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

    [Fact]
    public async Task ImageCreateGetAndTaskBodyPersistence_StoresTaskOwnedEditorImages()
    {
        await using var database = await TestDatabase.CreateAsync();
        var created = await database.Tasks.CreateAsync(CreateRequest("Task with image"), CancellationToken.None);
        var base64Data = Convert.ToBase64String([0x89, 0x50, 0x4e, 0x47]);

        var image = await database.Images.CreateAsync(new ImageCreateRequest(
            IssueId: null,
            TaskId: created.Id,
            Filename: "screenshot.png",
            MimeType: "image/png",
            Base64Data: base64Data,
            Width: null,
            Height: null), CancellationToken.None);

        Assert.StartsWith("app://image/", image.Src);

        var loadedImage = await database.Images.GetAsync(image.Id, CancellationToken.None);
        Assert.Equal("image/png", loadedImage.MimeType);
        Assert.Equal(base64Data, loadedImage.Base64Data);
        Assert.Equal("screenshot.png", loadedImage.Filename);

        await database.Tasks.UpdateAsync(new TaskSaveRequest(
            Id: created.Id,
            Title: created.Title,
            TaskTypeCode: created.TaskTypeCode,
            Body: $"""<p>See image</p><img src="{image.Src}" alt="screenshot.png">""",
            BodyFormatCode: "HTML",
            TaskPriorityCode: created.TaskPriorityCode,
            TaskSourceCode: null,
            SourceReference: null,
            SourceUrl: null,
            Deadline: null), CancellationToken.None);

        var reloaded = await database.Tasks.GetAsync(created.Id, CancellationToken.None);
        Assert.Contains(image.Src, reloaded.Body);
    }

    [Fact]
    public async Task ImageCreate_ValidatesTaskOwnerTypeAndSize()
    {
        await using var database = await TestDatabase.CreateAsync();
        var created = await database.Tasks.CreateAsync(CreateRequest("Task with invalid images"), CancellationToken.None);

        var unsupported = await Assert.ThrowsAsync<BridgeException>(() =>
            database.Images.CreateAsync(new ImageCreateRequest(
                IssueId: null,
                TaskId: created.Id,
                Filename: "file.bmp",
                MimeType: "image/bmp",
                Base64Data: Convert.ToBase64String([1, 2, 3]),
                Width: null,
                Height: null), CancellationToken.None));
        Assert.Equal("UnsupportedImageType", unsupported.Code);

        var tooLarge = await Assert.ThrowsAsync<BridgeException>(() =>
            database.Images.CreateAsync(new ImageCreateRequest(
                IssueId: null,
                TaskId: created.Id,
                Filename: "large.png",
                MimeType: "image/png",
                Base64Data: Convert.ToBase64String(new byte[(5 * 1024 * 1024) + 1]),
                Width: null,
                Height: null), CancellationToken.None));
        Assert.Equal("ImageTooLarge", tooLarge.Code);

        var missingTask = await Assert.ThrowsAsync<BridgeException>(() =>
            database.Images.CreateAsync(new ImageCreateRequest(
                IssueId: null,
                TaskId: 999,
                Filename: "missing.png",
                MimeType: "image/png",
                Base64Data: Convert.ToBase64String([1, 2, 3]),
                Width: null,
                Height: null), CancellationToken.None));
        Assert.Equal("NotFound", missingTask.Code);
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
