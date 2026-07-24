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

        var orderedTypeCodes = createdLookupSettings
            .GetProperty("taskTypes")
            .EnumerateArray()
            .Select(item => item.GetProperty("code").GetString()!)
            .ToList();
        orderedTypeCodes.Remove("QUESTION");
        orderedTypeCodes.Insert(0, "QUESTION");
        var reorderedLookupSettings = await fixture.SendAsync("lookup.settings.reorder", new
        {
            group = "taskTypes",
            orderedCodes = orderedTypeCodes
        });
        Assert.Equal(
            "QUESTION",
            reorderedLookupSettings.GetProperty("taskTypes").EnumerateArray().First().GetProperty("code").GetString());

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

        var attachmentBytes = new byte[] { 10, 20, 30 };
        var attachments = await fixture.SendAsync("task.attachment.create", new
        {
            taskId,
            fileName = "build.log",
            contentType = "text/plain",
            base64Data = Convert.ToBase64String(attachmentBytes),
            description = (string?)null
        });
        var attachment = Assert.Single(attachments.EnumerateArray());
        var attachmentId = attachment.GetProperty("id").GetInt32();
        Assert.Equal("build.log", attachment.GetProperty("fileName").GetString());

        var attachmentContent = await fixture.SendAsync("task.attachment.get", new { attachmentId });
        Assert.Equal(Convert.ToBase64String(attachmentBytes), attachmentContent.GetProperty("base64Data").GetString());

        attachments = await fixture.SendAsync("task.attachment.delete", new { taskId, attachmentId });
        Assert.Empty(attachments.EnumerateArray());

        var checklist = await fixture.SendAsync("task.checklist.create", new { taskId, text = "Check logs" });
        var firstChecklistId = Assert.Single(checklist.EnumerateArray()).GetProperty("id").GetInt32();
        checklist = await fixture.SendAsync("task.checklist.create", new { taskId, text = "Verify deployment" });
        var checklistItems = checklist.EnumerateArray().ToList();
        var secondChecklistId = checklistItems.Single(item => item.GetProperty("text").GetString() == "Verify deployment").GetProperty("id").GetInt32();

        checklist = await fixture.SendAsync("task.checklist.complete", new
        {
            taskId,
            checklistItemId = firstChecklistId,
            isCompleted = true
        });
        var completedChecklistItem = checklist.EnumerateArray().Single(item => item.GetProperty("id").GetInt32() == firstChecklistId);
        Assert.True(completedChecklistItem.GetProperty("isCompleted").GetBoolean());
        Assert.NotEqual(JsonValueKind.Null, completedChecklistItem.GetProperty("completedAt").ValueKind);

        checklist = await fixture.SendAsync("task.checklist.reorder", new
        {
            taskId,
            orderedChecklistItemIds = new[] { secondChecklistId, firstChecklistId }
        });
        Assert.Equal(secondChecklistId, checklist.EnumerateArray().First().GetProperty("id").GetInt32());

        checklist = await fixture.SendAsync("task.checklist.update", new
        {
            taskId,
            checklistItemId = secondChecklistId,
            text = "Verify production deployment"
        });
        Assert.Contains(checklist.EnumerateArray(), item => item.GetProperty("text").GetString() == "Verify production deployment");

        activeList = await fixture.SendAsync("task.list", new { view = "active" });
        var checklistTask = Assert.Single(activeList.EnumerateArray(), task => task.GetProperty("id").GetInt32() == taskId);
        Assert.Equal(1, checklistTask.GetProperty("completedChecklistCount").GetInt32());
        Assert.Equal(2, checklistTask.GetProperty("checklistCount").GetInt32());

        checklist = await fixture.SendAsync("task.checklist.complete", new
        {
            taskId,
            checklistItemId = firstChecklistId,
            isCompleted = false
        });
        Assert.False(checklist.EnumerateArray().Single(item => item.GetProperty("id").GetInt32() == firstChecklistId).GetProperty("isCompleted").GetBoolean());

        checklist = await fixture.SendAsync("task.checklist.delete", new { taskId, checklistItemId = secondChecklistId });
        Assert.Single(checklist.EnumerateArray());

        var relatedTask = await fixture.SendAsync("task.create", new
        {
            title = "Related bridge task",
            taskTypeCode = "NOTE",
            body = "",
            bodyFormatCode = "HTML",
            taskPriorityCode = (string?)null,
            taskSourceCode = (string?)null,
            sourceReference = (string?)null,
            sourceUrl = (string?)null,
            deadline = (DateTime?)null
        });
        var relatedTaskId = relatedTask.GetProperty("id").GetInt32();
        var relationships = await fixture.SendAsync("task.relation.create", new
        {
            taskId,
            targetTaskId = relatedTaskId,
            relationTypeCode = "BLOCKS"
        });
        var relationship = Assert.Single(relationships.EnumerateArray());
        Assert.Equal("Blocks", relationship.GetProperty("relationName").GetString());
        var relationId = relationship.GetProperty("id").GetInt32();

        var reverseRelationships = await fixture.SendAsync("task.relation.list", new { taskId = relatedTaskId });
        Assert.Equal("Blocked by", Assert.Single(reverseRelationships.EnumerateArray()).GetProperty("relationName").GetString());

        relationships = await fixture.SendAsync("task.relation.delete", new { taskId, relationId });
        Assert.Empty(relationships.EnumerateArray());

        var initialTimeline = await fixture.SendAsync("task.timeline.get", new { taskId });
        Assert.Contains(
            initialTimeline.EnumerateArray(),
            item => item.GetProperty("kind").GetString() == "log"
                && item.GetProperty("logTypeCode").GetString() == TaskLogTypeCodes.TaskCreated);
        Assert.Contains(initialTimeline.EnumerateArray(), item => item.GetProperty("logTypeCode").GetString() == "ATTACHMENT_ADDED");
        Assert.Contains(initialTimeline.EnumerateArray(), item => item.GetProperty("logTypeCode").GetString() == "ATTACHMENT_REMOVED");
        Assert.Contains(initialTimeline.EnumerateArray(), item => item.GetProperty("logTypeCode").GetString() == "CHECKLIST_ITEM_ADDED");
        Assert.Contains(initialTimeline.EnumerateArray(), item => item.GetProperty("logTypeCode").GetString() == "CHECKLIST_ITEM_COMPLETED");
        Assert.Contains(initialTimeline.EnumerateArray(), item => item.GetProperty("logTypeCode").GetString() == "CHECKLIST_ITEM_REOPENED");
        Assert.Contains(initialTimeline.EnumerateArray(), item => item.GetProperty("logTypeCode").GetString() == "RELATION_ADDED");
        Assert.Contains(initialTimeline.EnumerateArray(), item => item.GetProperty("logTypeCode").GetString() == "RELATION_REMOVED");

        var relatedTaskTimeline = await fixture.SendAsync("task.timeline.get", new { taskId = relatedTaskId });
        Assert.Contains(
            relatedTaskTimeline.EnumerateArray(),
            item => item.GetProperty("logTypeCode").GetString() == "RELATION_ADDED"
                && item.GetProperty("text").GetString() == "Relationship added: Blocked by Bridge task");
        Assert.Contains(
            relatedTaskTimeline.EnumerateArray(),
            item => item.GetProperty("logTypeCode").GetString() == "RELATION_REMOVED"
                && item.GetProperty("text").GetString() == "Relationship removed: Blocked by Bridge task");

        var timelineWithComment = await fixture.SendAsync("task.comment.create", new
        {
            taskId,
            commentText = "Bridge comment"
        });
        var comment = Assert.Single(
            timelineWithComment.EnumerateArray(),
            item => item.GetProperty("kind").GetString() == "comment");
        Assert.Equal("Bridge comment", comment.GetProperty("text").GetString());
        Assert.True(comment.GetProperty("canDelete").GetBoolean());

        var timelineAfterCommentDelete = await fixture.SendAsync("task.comment.delete", new
        {
            taskId,
            commentId = comment.GetProperty("id").GetInt32()
        });
        Assert.DoesNotContain(
            timelineAfterCommentDelete.EnumerateArray(),
            item => item.GetProperty("kind").GetString() == "comment");

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

        var completedAfterCancel = await fixture.SendAsync("task.list", new { view = "completed" });
        Assert.DoesNotContain(
            completedAfterCancel.EnumerateArray(),
            task => task.GetProperty("id").GetInt32() == cancelled.GetProperty("id").GetInt32());

        var allAfterCancel = await fixture.SendAsync("task.list", new { view = "all" });
        Assert.Contains(
            allAfterCancel.EnumerateArray(),
            task => task.GetProperty("id").GetInt32() == cancelled.GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task OkfCommandRunner_UsesSharedApplicationServiceAndCreatesTaskLogs()
    {
        await using var fixture = await BridgeFixture.CreateAsync();
        var created = await fixture.SendAsync("task.create", new
        {
            title = "Shared command task",
            taskTypeCode = "REQUEST",
            body = "",
            bodyFormatCode = "HTML",
            taskPriorityCode = "NORMAL",
            taskSourceCode = (string?)null,
            sourceReference = (string?)null,
            sourceUrl = (string?)null,
            deadline = (DateTime?)null
        });
        var taskId = created.GetProperty("id").GetInt32();

        var updated = await fixture.SendOkfAsync("task.update", new
        {
            id = taskId,
            title = "Updated through OKF command",
            taskTypeCode = "REQUEST",
            body = "",
            bodyFormatCode = "HTML",
            taskPriorityCode = "NORMAL",
            taskSourceCode = (string?)null,
            sourceReference = (string?)null,
            sourceUrl = (string?)null,
            deadline = (DateTime?)null
        });

        Assert.Equal("Updated through OKF command", updated.GetProperty("title").GetString());

        var timeline = await fixture.SendOkfAsync("task.timeline.get", new { taskId });
        Assert.Contains(
            timeline.EnumerateArray(),
            item => item.GetProperty("logTypeCode").GetString() == TaskLogTypeCodes.TaskUpdated
                && item.GetProperty("oldValue").GetString() == "Shared command task"
                && item.GetProperty("newValue").GetString() == "Updated through OKF command");
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
    public async Task Bridge_ListsUrgentWaitingAndOverdueViews()
    {
        await using var fixture = await BridgeFixture.CreateAsync();

        var urgent = await fixture.SendAsync("task.create", new
        {
            title = "Urgent bridge task",
            taskTypeCode = "REQUEST",
            body = "",
            bodyFormatCode = "HTML",
            taskPriorityCode = TaskPriorityCodes.Urgent,
            taskSourceCode = (string?)null,
            sourceReference = (string?)null,
            sourceUrl = (string?)null,
            deadline = (DateTime?)null
        });
        var waiting = await fixture.SendAsync("task.create", new
        {
            title = "Waiting bridge task",
            taskTypeCode = "REQUEST",
            body = "",
            bodyFormatCode = "HTML",
            taskPriorityCode = "NORMAL",
            taskSourceCode = (string?)null,
            sourceReference = (string?)null,
            sourceUrl = (string?)null,
            deadline = (DateTime?)null,
            activeWaitingForLabel = "External response"
        });
        var overdue = await fixture.SendAsync("task.create", new
        {
            title = "Overdue bridge task",
            taskTypeCode = "REQUEST",
            body = "",
            bodyFormatCode = "HTML",
            taskPriorityCode = "NORMAL",
            taskSourceCode = (string?)null,
            sourceReference = (string?)null,
            sourceUrl = (string?)null,
            deadline = DateTime.UtcNow.Date.AddDays(-1)
        });

        var urgentList = await fixture.SendAsync("task.list", new { view = "urgent" });
        Assert.Equal(urgent.GetProperty("id").GetInt32(), Assert.Single(urgentList.EnumerateArray()).GetProperty("id").GetInt32());

        var waitingList = await fixture.SendAsync("task.list", new { view = "waiting" });
        Assert.Equal(waiting.GetProperty("id").GetInt32(), Assert.Single(waitingList.EnumerateArray()).GetProperty("id").GetInt32());

        var overdueList = await fixture.SendAsync("task.list", new { view = "overdue" });
        Assert.Equal(overdue.GetProperty("id").GetInt32(), Assert.Single(overdueList.EnumerateArray()).GetProperty("id").GetInt32());
    }

    [Fact]
    public async Task Bridge_DispatchesTagAdministrationMessages()
    {
        await using var fixture = await BridgeFixture.CreateAsync();
        await fixture.SendAsync("task.create", new
        {
            title = "Tagged bridge task",
            taskTypeCode = "REQUEST",
            body = "",
            bodyFormatCode = "HTML",
            taskPriorityCode = "NORMAL",
            taskSourceCode = (string?)null,
            sourceReference = (string?)null,
            sourceUrl = (string?)null,
            deadline = (DateTime?)null,
            tags = new[] { "Source", "Target" }
        });

        var tags = await fixture.SendAsync("tag.settings.list", new { });
        var source = tags.EnumerateArray().Single(tag => tag.GetProperty("value").GetString() == "Source");
        var target = tags.EnumerateArray().Single(tag => tag.GetProperty("value").GetString() == "Target");

        tags = await fixture.SendAsync("tag.settings.rename", new
        {
            tagId = target.GetProperty("id").GetInt32(),
            value = "Destination"
        });
        target = tags.EnumerateArray().Single(tag => tag.GetProperty("value").GetString() == "Destination");

        tags = await fixture.SendAsync("tag.settings.merge", new
        {
            sourceTagId = source.GetProperty("id").GetInt32(),
            targetTagId = target.GetProperty("id").GetInt32()
        });
        Assert.DoesNotContain(tags.EnumerateArray(), tag => tag.GetProperty("value").GetString() == "Source");
        Assert.Equal(1, Assert.Single(tags.EnumerateArray()).GetProperty("usageCount").GetInt32());
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
        Assert.Equal("MARKDOWN", initial.GetProperty("markdownEditType").GetString());
        Assert.Equal(360, initial.GetProperty("editorHeight").GetInt32());

        var saved = await fixture.SendAsync("editor.preference.save", new
        {
            bodyFormatCode = "MARKDOWN",
            markdownEditType = "WYSIWYG",
            editorHeight = 640
        });
        Assert.Equal("MARKDOWN", saved.GetProperty("bodyFormatCode").GetString());
        Assert.Equal("WYSIWYG", saved.GetProperty("markdownEditType").GetString());
        Assert.Equal(640, saved.GetProperty("editorHeight").GetInt32());

        var loaded = await fixture.SendAsync("editor.preference.get", new { });
        Assert.Equal("MARKDOWN", loaded.GetProperty("bodyFormatCode").GetString());
        Assert.Equal("WYSIWYG", loaded.GetProperty("markdownEditType").GetString());
        Assert.Equal(640, loaded.GetProperty("editorHeight").GetInt32());
    }

    [Fact]
    public async Task Bridge_PersistsMarkdownEditTypeWithoutChangingBodyFormat()
    {
        await using var fixture = await BridgeFixture.CreateAsync();

        await fixture.SendAsync("editor.preference.save", new
        {
            bodyFormatCode = "MARKDOWN",
            editorHeight = 512
        });

        var saved = await fixture.SendAsync("editor.preference.save", new
        {
            markdownEditType = "WYSIWYG"
        });

        Assert.Equal("MARKDOWN", saved.GetProperty("bodyFormatCode").GetString());
        Assert.Equal("WYSIWYG", saved.GetProperty("markdownEditType").GetString());
        Assert.Equal(512, saved.GetProperty("editorHeight").GetInt32());

        var loaded = await fixture.SendAsync("editor.preference.get", new { });
        Assert.Equal("MARKDOWN", loaded.GetProperty("bodyFormatCode").GetString());
        Assert.Equal("WYSIWYG", loaded.GetProperty("markdownEditType").GetString());
        Assert.Equal(512, loaded.GetProperty("editorHeight").GetInt32());
    }

    [Fact]
    public async Task Bridge_PersistsLayoutPreference()
    {
        await using var fixture = await BridgeFixture.CreateAsync();

        var initial = await fixture.SendAsync("layout.preference.get", new { });
        Assert.Equal(JsonValueKind.Null, initial.GetProperty("taskListWidth").ValueKind);
        Assert.Equal(JsonValueKind.Null, initial.GetProperty("taskListHeight").ValueKind);
        Assert.Equal("AUTO", initial.GetProperty("layoutMode").GetString());
        Assert.False(initial.GetProperty("showSourceFields").GetBoolean());
        Assert.False(initial.GetProperty("showOwner").GetBoolean());
        Assert.False(initial.GetProperty("showResponsible").GetBoolean());
        Assert.False(initial.GetProperty("showRelationships").GetBoolean());
        Assert.False(initial.GetProperty("allowEditingCompletedTasks").GetBoolean());
        Assert.False(initial.GetProperty("allowEditingCancelledTasks").GetBoolean());
        Assert.Equal("LIGHT", initial.GetProperty("colorScheme").GetString());
        Assert.Equal("ATTENTION", initial.GetProperty("taskSortModes").GetProperty("active").GetString());
        Assert.Equal("ATTENTION", initial.GetProperty("taskSortModes").GetProperty("waiting").GetString());
        Assert.Equal("ASC", initial.GetProperty("taskSortDirections").GetProperty("active").GetString());
        Assert.Equal("ASC", initial.GetProperty("taskSortDirections").GetProperty("waiting").GetString());

        await fixture.SendAsync("editor.preference.save", new
        {
            bodyFormatCode = "MARKDOWN"
        });

        var saved = await fixture.SendAsync("layout.preference.save", new
        {
            taskListWidth = 412,
            taskListHeight = 275,
            layoutMode = "STACKED",
            showSourceFields = true,
            showOwner = true,
            showResponsible = false,
            showRelationships = true,
            allowEditingCompletedTasks = true,
            allowEditingCancelledTasks = false,
            colorScheme = "DARK",
            taskSortModes = new Dictionary<string, string>
            {
                ["active"] = "RECENTLY_UPDATED",
                ["waiting"] = "WAITING_LONGEST"
            },
            taskSortDirections = new Dictionary<string, string>
            {
                ["active"] = "DESC",
                ["waiting"] = "ASC"
            }
        });
        Assert.Equal(412, saved.GetProperty("taskListWidth").GetDouble());
        Assert.Equal(275, saved.GetProperty("taskListHeight").GetDouble());
        Assert.Equal("STACKED", saved.GetProperty("layoutMode").GetString());
        Assert.True(saved.GetProperty("showSourceFields").GetBoolean());
        Assert.True(saved.GetProperty("showOwner").GetBoolean());
        Assert.False(saved.GetProperty("showResponsible").GetBoolean());
        Assert.True(saved.GetProperty("showRelationships").GetBoolean());
        Assert.True(saved.GetProperty("allowEditingCompletedTasks").GetBoolean());
        Assert.False(saved.GetProperty("allowEditingCancelledTasks").GetBoolean());
        Assert.Equal("DARK", saved.GetProperty("colorScheme").GetString());
        Assert.Equal("RECENTLY_UPDATED", saved.GetProperty("taskSortModes").GetProperty("active").GetString());
        Assert.Equal("WAITING_LONGEST", saved.GetProperty("taskSortModes").GetProperty("waiting").GetString());
        Assert.Equal("ATTENTION", saved.GetProperty("taskSortModes").GetProperty("all").GetString());
        Assert.Equal("DESC", saved.GetProperty("taskSortDirections").GetProperty("active").GetString());
        Assert.Equal("ASC", saved.GetProperty("taskSortDirections").GetProperty("waiting").GetString());

        var loaded = await fixture.SendAsync("layout.preference.get", new { });
        Assert.Equal(412, loaded.GetProperty("taskListWidth").GetDouble());
        Assert.Equal(275, loaded.GetProperty("taskListHeight").GetDouble());
        Assert.Equal("STACKED", loaded.GetProperty("layoutMode").GetString());
        Assert.True(loaded.GetProperty("showSourceFields").GetBoolean());
        Assert.True(loaded.GetProperty("showOwner").GetBoolean());
        Assert.False(loaded.GetProperty("showResponsible").GetBoolean());
        Assert.True(loaded.GetProperty("showRelationships").GetBoolean());
        Assert.True(loaded.GetProperty("allowEditingCompletedTasks").GetBoolean());
        Assert.False(loaded.GetProperty("allowEditingCancelledTasks").GetBoolean());
        Assert.Equal("DARK", loaded.GetProperty("colorScheme").GetString());
        Assert.Equal("RECENTLY_UPDATED", loaded.GetProperty("taskSortModes").GetProperty("active").GetString());
        Assert.Equal("WAITING_LONGEST", loaded.GetProperty("taskSortModes").GetProperty("waiting").GetString());
        Assert.Equal("DESC", loaded.GetProperty("taskSortDirections").GetProperty("active").GetString());
        Assert.Equal("ASC", loaded.GetProperty("taskSortDirections").GetProperty("waiting").GetString());

        var editorPreference = await fixture.SendAsync("editor.preference.get", new { });
        Assert.Equal("MARKDOWN", editorPreference.GetProperty("bodyFormatCode").GetString());
        Assert.Equal("MARKDOWN", editorPreference.GetProperty("markdownEditType").GetString());
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
    public async Task Bridge_RejectsInvalidMarkdownEditTypePreference()
    {
        await using var fixture = await BridgeFixture.CreateAsync();

        using var response = await fixture.SendRawAsync("editor.preference.save", new
        {
            markdownEditType = "SIDE_BY_SIDE"
        });

        Assert.False(response.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("ValidationFailed", response.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("markdownEditType", response.RootElement.GetProperty("error").GetProperty("details").GetProperty("field").GetString());
    }

    [Fact]
    public async Task Bridge_RejectsInvalidEditorHeightPreference()
    {
        await using var fixture = await BridgeFixture.CreateAsync();

        using var response = await fixture.SendRawAsync("editor.preference.save", new
        {
            editorHeight = 120
        });

        Assert.False(response.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("ValidationFailed", response.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("editorHeight", response.RootElement.GetProperty("error").GetProperty("details").GetProperty("field").GetString());
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

    [Fact]
    public async Task Bridge_RejectsInvalidColorSchemePreference()
    {
        await using var fixture = await BridgeFixture.CreateAsync();

        using var response = await fixture.SendRawAsync("layout.preference.save", new
        {
            colorScheme = "SYSTEM"
        });

        Assert.False(response.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("ValidationFailed", response.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("colorScheme", response.RootElement.GetProperty("error").GetProperty("details").GetProperty("field").GetString());
    }

    [Fact]
    public async Task Bridge_RejectsInvalidTaskSortModePreference()
    {
        await using var fixture = await BridgeFixture.CreateAsync();

        using var response = await fixture.SendRawAsync("layout.preference.save", new
        {
            taskSortModes = new Dictionary<string, string>
            {
                ["active"] = "MAGIC"
            }
        });

        Assert.False(response.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("ValidationFailed", response.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("taskSortModes", response.RootElement.GetProperty("error").GetProperty("details").GetProperty("field").GetString());
    }

    [Fact]
    public async Task Bridge_RejectsInvalidTaskSortDirectionPreference()
    {
        await using var fixture = await BridgeFixture.CreateAsync();

        using var response = await fixture.SendRawAsync("layout.preference.save", new
        {
            taskSortDirections = new Dictionary<string, string>
            {
                ["active"] = "SIDEWAYS"
            }
        });

        Assert.False(response.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("ValidationFailed", response.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("taskSortDirections", response.RootElement.GetProperty("error").GetProperty("details").GetProperty("field").GetString());
    }

    [Fact]
    public async Task Bridge_CreatesDatabaseBackupContainingTaskData()
    {
        await using var fixture = await BridgeFixture.CreateAsync();
        await fixture.SendAsync("task.create", new
        {
            title = "Backup bridge task",
            taskTypeCode = "REQUEST",
            body = "",
            bodyFormatCode = "HTML",
            taskPriorityCode = "NORMAL",
            taskSourceCode = (string?)null,
            sourceReference = (string?)null,
            sourceUrl = (string?)null,
            deadline = (DateTime?)null
        });

        var result = await fixture.SendAsync("database.backup.create", new { });

        Assert.False(result.GetProperty("cancelled").GetBoolean());
        Assert.Equal(Path.GetFullPath(fixture.BackupPath), result.GetProperty("filePath").GetString());
        Assert.True(File.Exists(fixture.BackupPath));

        await using var backupConnection = new SqliteConnection(
            $"Data Source={fixture.BackupPath};Mode=ReadOnly;Pooling=False");
        await backupConnection.OpenAsync();
        await using var command = backupConnection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM TaskItems WHERE Title = 'Backup bridge task';";
        Assert.Equal(1L, Convert.ToInt64(await command.ExecuteScalarAsync()));
    }

    private sealed class BridgeFixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        private readonly string preferencesDirectory;
        private readonly ServiceProvider services;
        private readonly BridgeMessageHandler handler;
        private readonly OkfCommandRunner okfCommandRunner;
        private readonly TestBackupDestinationPicker backupDestinationPicker;

        private BridgeFixture(
            SqliteConnection connection,
            string preferencesDirectory,
            ServiceProvider services,
            BridgeMessageHandler handler,
            OkfCommandRunner okfCommandRunner,
            TestBackupDestinationPicker backupDestinationPicker)
        {
            this.connection = connection;
            this.preferencesDirectory = preferencesDirectory;
            this.services = services;
            this.handler = handler;
            this.okfCommandRunner = okfCommandRunner;
            this.backupDestinationPicker = backupDestinationPicker;
        }

        public string BackupPath => backupDestinationPicker.Path!;

        public static async Task<BridgeFixture> CreateAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var preferencesDirectory = Path.Combine(Path.GetTempPath(), "Okf-Todo.Tests", Guid.NewGuid().ToString("N"));
            var backupDestinationPicker = new TestBackupDestinationPicker(
                Path.Combine(preferencesDirectory, "bridge-backup.db"));

            var serviceCollection = new ServiceCollection();
            serviceCollection.AddLogging();
            serviceCollection.AddDbContext<AppDbContext>(options => options.UseSqlite(connection));
            serviceCollection.AddSingleton<IAppPreferencePathProvider>(
                new TestAppPreferencePathProvider(Path.Combine(preferencesDirectory, "app-preferences.json")));
            serviceCollection.AddSingleton<IBackupDestinationPicker>(backupDestinationPicker);
            serviceCollection.AddScoped<LookupSeedService>();
            serviceCollection.AddScoped<TaskLifecycleService>();
            serviceCollection.AddScoped<TaskService>();
            serviceCollection.AddScoped<TaskAttachmentService>();
            serviceCollection.AddScoped<TaskChecklistService>();
            serviceCollection.AddScoped<TaskRelationService>();
            serviceCollection.AddScoped<AppPreferenceService>();
            serviceCollection.AddScoped<ImageService>();
            serviceCollection.AddScoped<DatabaseBackupService>();
            serviceCollection.AddSingleton<ApplicationCommandService>();
            serviceCollection.AddSingleton<BridgeMessageHandler>();
            serviceCollection.AddSingleton<OkfCommandRunner>();

            var services = serviceCollection.BuildServiceProvider();

            await using (var scope = services.CreateAsyncScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await dbContext.Database.EnsureCreatedAsync();
                await scope.ServiceProvider.GetRequiredService<LookupSeedService>().SeedAsync();
            }

            var handler = services.GetRequiredService<BridgeMessageHandler>();
            var okfCommandRunner = services.GetRequiredService<OkfCommandRunner>();

            return new BridgeFixture(
                connection,
                preferencesDirectory,
                services,
                handler,
                okfCommandRunner,
                backupDestinationPicker);
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

        public async Task<JsonElement> SendOkfAsync(string type, object payload)
        {
            var request = JsonSerializer.Serialize(new
            {
                messageId = Guid.NewGuid().ToString("N"),
                type,
                payload
            }, JsonOptions);
            var result = await okfCommandRunner.RunAsync(request);

            using var response = JsonDocument.Parse(result.ResponseJson);
            Assert.True(result.Succeeded, response.RootElement.ToString());
            return response.RootElement.GetProperty("payload").Clone();
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

    private sealed class TestBackupDestinationPicker(string? path) : IBackupDestinationPicker
    {
        public string? Path { get; } = path;

        public Task<string?> PickAsync(
            string suggestedFileName,
            string? initialDirectory,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(Path);
        }
    }
}
