using Microsoft.EntityFrameworkCore;
using Photino.Okf_Todo.Services;

namespace Okf_Todo.Tests;

public sealed class TaskServiceTests
{
    [Fact]
    public async Task LookupSettings_ExposeSystemManagedGroupsButRejectsMutations()
    {
        await using var database = await TestDatabase.CreateAsync();

        var settings = await database.Tasks.GetLookupSettingsAsync(CancellationToken.None);

        Assert.Contains(settings.TaskSources, item => item.Code == "EMAIL");
        Assert.Contains(settings.TaskRelationTypes, item => item.Code == "BLOCKS" && item.ReverseName == "Blocked by");
        Assert.All(settings.BodyFormats, item => Assert.True(item.IsReadOnly));
        Assert.All(settings.TaskLogTypes, item => Assert.True(item.IsReadOnly));

        foreach (var group in new[] { "taskSources", "taskRelationTypes", "bodyFormats", "taskLogTypes" })
        {
            var updateException = await Assert.ThrowsAsync<ValidationException>(() => database.Tasks.UpdateLookupAsync(
                new LookupUpdateRequest(group, "ANY", "Any", null, 10, true, false, null, null, "Any reverse"),
                CancellationToken.None));
            Assert.Equal("group", updateException.Field);

            var createException = await Assert.ThrowsAsync<ValidationException>(() => database.Tasks.CreateLookupAsync(
                new LookupCreateRequest(group, "ANY", "Any", null, 10, true, false, null, null, "Any reverse"),
                CancellationToken.None));
            Assert.Equal("group", createException.Field);

            var deleteException = await Assert.ThrowsAsync<ValidationException>(() => database.Tasks.DeleteLookupAsync(
                new LookupDeleteRequest(group, "ANY"),
                CancellationToken.None));
            Assert.Equal("group", deleteException.Field);

            var reorderException = await Assert.ThrowsAsync<ValidationException>(() => database.Tasks.ReorderLookupAsync(
                new LookupReorderRequest(group, ["ANY"]),
                CancellationToken.None));
            Assert.Equal("group", reorderException.Field);
        }
    }

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
            Deadline: deadline,
            ActiveWaitingForLabel: "Initial wait",
            Tags: ["Initial"]), CancellationToken.None);

        Assert.Equal("Fix failed deployment", created.Title);
        Assert.Equal(TaskStatusCodes.Active, created.TaskStatusCode);
        Assert.Equal("ERROR", created.TaskTypeCode);
        Assert.Equal("NORMAL", created.TaskPriorityCode);
        Assert.Equal("EMAIL", created.TaskSourceCode);
        Assert.Equal("INC123456", created.SourceReference);
        Assert.Equal("https://example.test/inc/123456", created.SourceUrl);
        Assert.Equal("Initial wait", created.ActiveWaitingFor?.Label);
        var creationTimeline = await database.Tasks.GetTimelineAsync(new TaskTimelineRequest(created.Id), CancellationToken.None);
        Assert.Single(creationTimeline);
        Assert.Equal(TaskLogTypeCodes.TaskCreated, creationTimeline.Single().LogTypeCode);

        var activeTasks = await database.Tasks.ListAsync(new TaskListRequest("active"), CancellationToken.None);
        var listed = Assert.Single(activeTasks);
        Assert.Equal(created.Id, listed.Id);
        Assert.Equal("Fix failed deployment", listed.Title);
        Assert.Equal(TaskStatusCodes.Active, listed.TaskStatusCode);
        Assert.Equal("#facc15", listed.TaskTypeBackgroundColor);
        Assert.Equal("#111827", listed.TaskTypeForegroundColor);
        Assert.Equal("#6b7280", listed.TaskStatusBackgroundColor);
        Assert.Equal("#ffffff", listed.TaskStatusForegroundColor);
        Assert.Equal(["Initial"], listed.Tags);

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
            Deadline: updatedDeadline,
            ActiveWaitingForLabel: "Initial wait",
            Tags: ["Support"]), CancellationToken.None);

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
        Assert.Contains(savedTask.LogEntries, log => log.Message == "Title: Changed 'Fix failed deployment' to 'Investigate failed deployment'");
        Assert.Contains(savedTask.LogEntries, log => log.Message == "Source: Changed 'Email' to 'Teams'");
        Assert.Contains(savedTask.LogEntries, log => log.Message == "Source reference: Changed 'INC123456' to 'Release room'");
        Assert.Contains(savedTask.LogEntries, log => log.Message == "Source URL: Changed 'https://example.test/inc/123456' to '(none)'");
        Assert.Contains(savedTask.LogEntries, log => log.Message == "Editor changed");
        Assert.Contains(savedTask.LogEntries, log => log.Message == "Tags: Changed 'Initial' to 'Support'");
    }

    [Fact]
    public async Task CreateAndUpdate_AttachesCreatesAndRemovesStringTags()
    {
        await using var database = await TestDatabase.CreateAsync();

        var created = await database.Tasks.CreateAsync(CreateRequest("Tagged task") with
        {
            Tags = ["Deployment", "Oracle APEX"]
        }, CancellationToken.None);

        Assert.Equal(["Deployment", "Oracle APEX"], created.Tags);
        var lookups = await database.Tasks.GetLookupsAsync(CancellationToken.None);
        Assert.Contains("Deployment", lookups.Tags);
        Assert.Contains("Oracle APEX", lookups.Tags);

        var updated = await database.Tasks.UpdateAsync(CreateRequest("Tagged task") with
        {
            Id = created.Id,
            Tags = ["Oracle APEX", "Support"]
        }, CancellationToken.None);

        Assert.Equal(["Oracle APEX", "Support"], updated.Tags);
        Assert.DoesNotContain("Deployment", updated.Tags);
        Assert.Equal(3, await database.DbContext.TaskTags.CountAsync());
        Assert.Equal(2, await database.DbContext.TaskTaskTags.CountAsync());
    }

    [Fact]
    public async Task TagSettings_RenamesDeletesUnusedAndMergesUsedTags()
    {
        await using var database = await TestDatabase.CreateAsync();
        var first = await database.Tasks.CreateAsync(CreateRequest("First tagged task") with
        {
            Tags = ["Alpha", "Beta"]
        }, CancellationToken.None);
        var second = await database.Tasks.CreateAsync(CreateRequest("Second tagged task") with
        {
            Tags = ["Alpha"]
        }, CancellationToken.None);
        var unusedTask = await database.Tasks.CreateAsync(CreateRequest("Unused tag task") with
        {
            Tags = ["Orphan"]
        }, CancellationToken.None);
        await database.Tasks.UpdateAsync(CreateRequest("Unused tag task") with
        {
            Id = unusedTask.Id,
            Tags = []
        }, CancellationToken.None);

        var settings = await database.Tasks.GetTagSettingsAsync(CancellationToken.None);
        var alpha = Assert.Single(settings, tag => tag.Value == "Alpha");
        var beta = Assert.Single(settings, tag => tag.Value == "Beta");
        var orphan = Assert.Single(settings, tag => tag.Value == "Orphan");
        Assert.Equal(2, alpha.UsageCount);
        Assert.Equal(1, beta.UsageCount);
        Assert.Equal(0, orphan.UsageCount);

        settings = await database.Tasks.RenameTagAsync(
            new TagRenameRequest(orphan.Id, " Unused "),
            CancellationToken.None);
        var unused = Assert.Single(settings, tag => tag.Value == "Unused");

        var usedDeleteException = await Assert.ThrowsAsync<ValidationException>(() =>
            database.Tasks.DeleteTagAsync(new TagDeleteRequest(alpha.Id), CancellationToken.None));
        Assert.Equal("tagId", usedDeleteException.Field);

        settings = await database.Tasks.MergeTagAsync(
            new TagMergeRequest(alpha.Id, beta.Id),
            CancellationToken.None);
        Assert.DoesNotContain(settings, tag => tag.Id == alpha.Id);
        Assert.Equal(2, Assert.Single(settings, tag => tag.Id == beta.Id).UsageCount);
        Assert.Equal(["Beta"], (await database.Tasks.GetAsync(first.Id, CancellationToken.None)).Tags);
        Assert.Equal(["Beta"], (await database.Tasks.GetAsync(second.Id, CancellationToken.None)).Tags);
        Assert.Equal(2, await database.DbContext.TaskTaskTags.CountAsync());
        Assert.Contains(
            await database.Tasks.GetTimelineAsync(new TaskTimelineRequest(second.Id), CancellationToken.None),
            item => item.Text == "Tags: Changed 'Alpha' to 'Beta'");

        settings = await database.Tasks.DeleteTagAsync(
            new TagDeleteRequest(unused.Id),
            CancellationToken.None);
        Assert.DoesNotContain(settings, tag => tag.Id == unused.Id);
    }

    [Fact]
    public async Task List_FiltersUsefulViewsAndAppliesWorkPriorityOrder()
    {
        await using var database = await TestDatabase.CreateAsync();
        var today = DateTime.UtcNow.Date;

        var overdue = await database.Tasks.CreateAsync(CreateRequest("Overdue") with
        {
            Deadline = today.AddDays(-1)
        }, CancellationToken.None);
        var urgent = await database.Tasks.CreateAsync(CreateRequest("Urgent") with
        {
            TaskPriorityCode = TaskPriorityCodes.Urgent
        }, CancellationToken.None);
        var active = await database.Tasks.CreateAsync(CreateRequest("Active"), CancellationToken.None);
        var waiting = await database.Tasks.CreateAsync(CreateRequest("Waiting"), CancellationToken.None);
        await database.Tasks.AddWaitingForAsync(
            new TaskWaitingForSaveRequest(waiting.Id, "External response"),
            CancellationToken.None);
        var canWait = await database.Tasks.CreateAsync(CreateRequest("Can wait") with
        {
            TaskPriorityCode = TaskPriorityCodes.CanWait
        }, CancellationToken.None);
        var completed = await database.Tasks.CreateAsync(CreateRequest("Completed"), CancellationToken.None);
        await database.Tasks.CompleteAsync(completed.Id, CancellationToken.None);

        var activeTasks = await database.Tasks.ListAsync(new TaskListRequest("active"), CancellationToken.None);
        Assert.Equal(
            [overdue.Id, urgent.Id, active.Id, waiting.Id, canWait.Id],
            activeTasks.Select(task => task.Id));

        var urgentTasks = await database.Tasks.ListAsync(new TaskListRequest("urgent"), CancellationToken.None);
        Assert.Equal([urgent.Id], urgentTasks.Select(task => task.Id));

        var waitingTasks = await database.Tasks.ListAsync(new TaskListRequest("waiting"), CancellationToken.None);
        Assert.Equal([waiting.Id], waitingTasks.Select(task => task.Id));

        var overdueTasks = await database.Tasks.ListAsync(new TaskListRequest("overdue"), CancellationToken.None);
        Assert.Equal([overdue.Id], overdueTasks.Select(task => task.Id));

        var completedTasks = await database.Tasks.ListAsync(new TaskListRequest("completed"), CancellationToken.None);
        Assert.Equal([completed.Id], completedTasks.Select(task => task.Id));

        var allTasks = await database.Tasks.ListAsync(new TaskListRequest("all"), CancellationToken.None);
        Assert.Equal(
            [overdue.Id, urgent.Id, active.Id, waiting.Id, canWait.Id, completed.Id],
            allTasks.Select(task => task.Id));
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
        Assert.DoesNotContain(completedTasks, task => task.Id == cancelled.Id);

        var allTasks = await database.Tasks.ListAsync(new TaskListRequest("all"), CancellationToken.None);
        Assert.Contains(allTasks, task => task.Id == cancelled.Id && task.TaskStatusCode == TaskStatusCodes.Cancelled);
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
