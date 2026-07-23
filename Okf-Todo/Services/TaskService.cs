using Microsoft.EntityFrameworkCore;
using Photino.Okf_Todo.Data;

namespace Photino.Okf_Todo.Services;

public sealed class TaskService(AppDbContext dbContext, TaskLifecycleService lifecycleService)
{
    public async Task<TaskLookupsDto> GetLookupsAsync(CancellationToken cancellationToken)
    {
        return new TaskLookupsDto(
            await GetLookupItemsAsync(dbContext.TaskTypes, cancellationToken),
            await GetLookupItemsAsync(dbContext.TaskPriorities, cancellationToken),
            await GetLookupItemsAsync(dbContext.TaskSources, cancellationToken),
            await GetLookupItemsAsync(dbContext.BodyFormats, cancellationToken),
            await dbContext.TaskTags.AsNoTracking().OrderBy(tag => tag.Value).Select(tag => tag.Value).ToListAsync(cancellationToken));
    }

    public async Task<IReadOnlyCollection<TagSettingsItemDto>> GetTagSettingsAsync(
        CancellationToken cancellationToken)
    {
        return await dbContext.TaskTags
            .AsNoTracking()
            .OrderBy(tag => tag.Value)
            .Select(tag => new TagSettingsItemDto(tag.Id, tag.Value, tag.Tasks.Count))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<TagSettingsItemDto>> RenameTagAsync(
        TagRenameRequest request,
        CancellationToken cancellationToken)
    {
        var value = NormalizeTagValue(request.Value);
        var tag = await dbContext.TaskTags
            .SingleOrDefaultAsync(item => item.Id == request.TagId, cancellationToken)
            ?? throw new ValidationException("Tag was not found.", "tagId");

        if (await dbContext.TaskTags.AnyAsync(
            item => item.Id != tag.Id && item.Value == value,
            cancellationToken))
        {
            throw new ValidationException("A tag with this value already exists. Merge the tags instead.", "value");
        }

        tag.Value = value;
        await dbContext.SaveChangesAsync(cancellationToken);
        return await GetTagSettingsAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<TagSettingsItemDto>> DeleteTagAsync(
        TagDeleteRequest request,
        CancellationToken cancellationToken)
    {
        var tag = await dbContext.TaskTags
            .Include(item => item.Tasks)
            .SingleOrDefaultAsync(item => item.Id == request.TagId, cancellationToken)
            ?? throw new ValidationException("Tag was not found.", "tagId");

        if (tag.Tasks.Count != 0)
        {
            throw new ValidationException("A used tag cannot be deleted. Merge it into another tag instead.", "tagId");
        }

        dbContext.TaskTags.Remove(tag);
        await dbContext.SaveChangesAsync(cancellationToken);
        return await GetTagSettingsAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<TagSettingsItemDto>> MergeTagAsync(
        TagMergeRequest request,
        CancellationToken cancellationToken)
    {
        if (request.SourceTagId == request.TargetTagId)
        {
            throw new ValidationException("Select a different target tag.", "targetTagId");
        }

        var source = await dbContext.TaskTags
            .SingleOrDefaultAsync(item => item.Id == request.SourceTagId, cancellationToken)
            ?? throw new ValidationException("Source tag was not found.", "sourceTagId");
        var target = await dbContext.TaskTags
            .SingleOrDefaultAsync(item => item.Id == request.TargetTagId, cancellationToken)
            ?? throw new ValidationException("Target tag was not found.", "targetTagId");
        var affectedTaskIds = await dbContext.TaskTaskTags
            .Where(item => item.TaskTagId == source.Id)
            .Select(item => item.TaskId)
            .ToListAsync(cancellationToken);
        var affectedTasks = await dbContext.TaskItems
            .Include(task => task.Tags)
                .ThenInclude(taskTag => taskTag.TaskTag)
            .Where(task => affectedTaskIds.Contains(task.Id))
            .ToListAsync(cancellationToken);
        var updateLogType = affectedTasks.Count == 0
            ? null
            : await GetOrCreateTaskUpdatedLogTypeAsync(cancellationToken);
        var now = DateTime.UtcNow;

        foreach (var task in affectedTasks)
        {
            var oldValues = task.Tags
                .Where(item => item.TaskTag is not null)
                .Select(item => item.TaskTag!.Value)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var sourceLink = task.Tags.Single(item => item.TaskTagId == source.Id);
            var hasTarget = task.Tags.Any(item => item.TaskTagId == target.Id);

            dbContext.TaskTaskTags.Remove(sourceLink);
            if (!hasTarget)
            {
                task.Tags.Add(new TaskTaskTag
                {
                    TaskTagId = target.Id,
                    TaskTag = target
                });
            }

            var newValues = oldValues
                .Where(value => !string.Equals(value, source.Value, StringComparison.OrdinalIgnoreCase))
                .Append(target.Value)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToList();

            AddFieldChangeLog(
                task,
                updateLogType!,
                "Tags",
                string.Join(", ", oldValues),
                string.Join(", ", newValues),
                now);
            task.UpdatedAt = now;
        }

        dbContext.TaskTags.Remove(source);
        await dbContext.SaveChangesAsync(cancellationToken);
        return await GetTagSettingsAsync(cancellationToken);
    }

    public async Task<TaskLookupSettingsDto> GetLookupSettingsAsync(CancellationToken cancellationToken)
    {
        var usedTaskTypeIds = (await dbContext.TaskItems
            .AsNoTracking()
            .Select(task => task.TaskTypeId)
            .Distinct()
            .ToListAsync(cancellationToken))
            .ToHashSet();
        var usedTaskPriorityIds = (await dbContext.TaskItems
            .AsNoTracking()
            .Where(task => task.TaskPriorityId.HasValue)
            .Select(task => task.TaskPriorityId!.Value)
            .Distinct()
            .ToListAsync(cancellationToken))
            .ToHashSet();
        var usedTaskStatusIds = (await dbContext.TaskItems
            .AsNoTracking()
            .Select(task => task.TaskStatusId)
            .Distinct()
            .ToListAsync(cancellationToken))
            .ToHashSet();
        var usedTaskSourceIds = (await dbContext.TaskItems.AsNoTracking().Where(task => task.TaskSourceId.HasValue)
            .Select(task => task.TaskSourceId!.Value).Distinct().ToListAsync(cancellationToken)).ToHashSet();
        var usedRelationTypeIds = (await dbContext.TaskRelations.AsNoTracking()
            .Select(relation => relation.TaskRelationTypeId).Distinct().ToListAsync(cancellationToken)).ToHashSet();
        var usedBodyFormatIds = (await dbContext.TaskItems.AsNoTracking().Where(task => task.BodyFormatId.HasValue)
            .Select(task => task.BodyFormatId!.Value).Distinct().ToListAsync(cancellationToken)).ToHashSet();
        var usedLogTypeIds = (await dbContext.TaskLogEntries.AsNoTracking()
            .Select(log => log.TaskLogTypeId).Distinct().ToListAsync(cancellationToken)).ToHashSet();

        return new TaskLookupSettingsDto(
            await GetLookupSettingsItemsAsync(dbContext.TaskTypes, usedTaskTypeIds, cancellationToken),
            await GetLookupSettingsItemsAsync(dbContext.TaskPriorities, usedTaskPriorityIds, cancellationToken),
            await GetLookupSettingsItemsAsync(dbContext.TaskStatuses, usedTaskStatusIds, cancellationToken),
            await GetLookupSettingsItemsAsync(dbContext.TaskSources, usedTaskSourceIds, cancellationToken),
            await GetRelationTypeSettingsAsync(usedRelationTypeIds, cancellationToken),
            await GetLookupSettingsItemsAsync(dbContext.BodyFormats, usedBodyFormatIds, cancellationToken, isReadOnly: true),
            await GetLookupSettingsItemsAsync(dbContext.TaskLogTypes, usedLogTypeIds, cancellationToken, isReadOnly: true));
    }

    public async Task<TaskLookupSettingsDto> UpdateLookupAsync(
        LookupUpdateRequest request,
        CancellationToken cancellationToken)
    {
        var group = request.Group.Trim();

        switch (group)
        {
            case "taskTypes":
                await UpdateLookupAsync(dbContext.TaskTypes, request, cancellationToken);
                break;
            case "taskPriorities":
                await UpdateLookupAsync(dbContext.TaskPriorities, request, cancellationToken);
                break;
            case "taskStatuses":
                await UpdateTaskStatusLookupAsync(request, cancellationToken);
                break;
            default:
                throw new ValidationException("Lookup group is not editable.", "group");
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return await GetLookupSettingsAsync(cancellationToken);
    }

    public async Task<TaskLookupSettingsDto> DeleteLookupAsync(
        LookupDeleteRequest request,
        CancellationToken cancellationToken)
    {
        var group = request.Group.Trim();

        switch (group)
        {
            case "taskTypes":
                await DeleteLookupAsync(
                    dbContext.TaskTypes,
                    request.Code,
                    (id, token) => dbContext.TaskItems.AnyAsync(task => task.TaskTypeId == id, token),
                    cancellationToken);
                break;
            case "taskPriorities":
                await DeleteLookupAsync(
                    dbContext.TaskPriorities,
                    request.Code,
                    (id, token) => dbContext.TaskItems.AnyAsync(task => task.TaskPriorityId == id, token),
                    cancellationToken);
                break;
            case "taskStatuses":
                await DeleteLookupAsync(
                    dbContext.TaskStatuses,
                    request.Code,
                    (id, token) => dbContext.TaskItems.AnyAsync(task => task.TaskStatusId == id, token),
                    cancellationToken);
                break;
            default:
                throw new ValidationException("Lookup group is not editable.", "group");
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return await GetLookupSettingsAsync(cancellationToken);
    }

    public async Task<TaskLookupSettingsDto> ReorderLookupAsync(
        LookupReorderRequest request,
        CancellationToken cancellationToken)
    {
        var group = request.Group.Trim();

        switch (group)
        {
            case "taskTypes":
                await ReorderLookupAsync(dbContext.TaskTypes, request, cancellationToken);
                break;
            case "taskPriorities":
                await ReorderLookupAsync(dbContext.TaskPriorities, request, cancellationToken);
                break;
            case "taskStatuses":
                await ReorderLookupAsync(dbContext.TaskStatuses, request, cancellationToken);
                break;
            default:
                throw new ValidationException("Lookup group is not editable.", "group");
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return await GetLookupSettingsAsync(cancellationToken);
    }

    public async Task<TaskLookupSettingsDto> CreateLookupAsync(
        LookupCreateRequest request,
        CancellationToken cancellationToken)
    {
        var group = request.Group.Trim();

        switch (group)
        {
            case "taskTypes":
                await CreateLookupAsync(dbContext.TaskTypes, request, cancellationToken);
                break;
            case "taskPriorities":
                await CreateLookupAsync(dbContext.TaskPriorities, request, cancellationToken);
                break;
            case "taskStatuses":
                await CreateLookupAsync(dbContext.TaskStatuses, request, cancellationToken);
                break;
            default:
                throw new ValidationException("Lookup group is not editable.", "group");
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return await GetLookupSettingsAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<TaskListItemDto>> ListAsync(
        TaskListRequest request,
        CancellationToken cancellationToken)
    {
        var view = string.IsNullOrWhiteSpace(request.View) ? "active" : request.View.Trim().ToLowerInvariant();
        var today = DateTime.UtcNow.Date;
        var query = dbContext.TaskItems
            .AsNoTracking()
            .Include(task => task.TaskType)
            .Include(task => task.TaskStatus)
            .Include(task => task.TaskPriority)
            .AsQueryable();

        query = view switch
        {
            "active" => query.Where(task => task.TaskStatus != null
                && task.TaskStatus.Code == TaskStatusCodes.Active),
            "urgent" => query.Where(task => task.TaskStatus != null
                && task.TaskStatus.Code == TaskStatusCodes.Active
                && task.TaskPriority != null
                && task.TaskPriority.Code == TaskPriorityCodes.Urgent),
            "waiting" => query.Where(task => task.TaskStatus != null
                && task.TaskStatus.Code == TaskStatusCodes.Active
                && task.WaitingTargets.Any(waitingFor => waitingFor.ResolvedAt == null)),
            "overdue" => query.Where(task => task.TaskStatus != null
                && task.TaskStatus.Code == TaskStatusCodes.Active
                && task.Deadline != null
                && task.Deadline < today),
            "completed" => query.Where(task => task.TaskStatus != null
                && task.TaskStatus.Code == TaskStatusCodes.Completed),
            "all" => query,
            _ => query.Where(task => task.TaskStatus != null
                && task.TaskStatus.Code == TaskStatusCodes.Active)
        };

        return await query
            .OrderBy(task => task.TaskStatus!.Code == TaskStatusCodes.Active
                    && task.Deadline != null
                    && task.Deadline < today
                ? 0
                : task.TaskStatus.Code == TaskStatusCodes.Active
                    && task.TaskPriority != null
                    && task.TaskPriority.Code == TaskPriorityCodes.Urgent
                ? 1
                : task.TaskStatus.Code == TaskStatusCodes.Active
                    && !task.WaitingTargets.Any(waitingFor => waitingFor.ResolvedAt == null)
                    && (task.TaskPriority == null || task.TaskPriority.Code != TaskPriorityCodes.CanWait)
                ? 2
                : task.TaskStatus.Code == TaskStatusCodes.Active
                    && task.WaitingTargets.Any(waitingFor => waitingFor.ResolvedAt == null)
                ? 3
                : task.TaskStatus.Code == TaskStatusCodes.Active
                    && task.TaskPriority != null
                    && task.TaskPriority.Code == TaskPriorityCodes.CanWait
                ? 4
                : 5)
            .ThenBy(task => task.Deadline == null)
            .ThenBy(task => task.Deadline)
            .ThenByDescending(task => task.UpdatedAt)
            .Select(task => new TaskListItemDto(
                task.Id,
                task.Title,
                task.TaskType!.Code,
                task.TaskType!.Name,
                task.TaskType.SortOrder,
                task.TaskType.BackgroundColor,
                task.TaskType.ForegroundColor,
                task.TaskStatus!.Code,
                task.TaskStatus.Name,
                task.TaskStatus.SortOrder,
                task.TaskStatus.BackgroundColor,
                task.TaskStatus.ForegroundColor,
                task.TaskPriority == null ? null : task.TaskPriority.Code,
                task.TaskPriority == null ? null : task.TaskPriority.Name,
                task.TaskPriority == null ? null : task.TaskPriority.SortOrder,
                task.TaskPriority == null ? null : task.TaskPriority.BackgroundColor,
                task.TaskPriority == null ? null : task.TaskPriority.ForegroundColor,
                task.Deadline,
                task.WaitingTargets
                    .Where(waitingFor => waitingFor.ResolvedAt == null)
                    .Select(waitingFor => waitingFor.Label)
                    .SingleOrDefault(),
                task.WaitingSince,
                task.ChecklistItems.Count(item => item.IsCompleted),
                task.ChecklistItems.Count,
                task.Tags
                    .Where(taskTag => taskTag.TaskTag != null)
                    .Select(taskTag => taskTag.TaskTag!.Value)
                    .OrderBy(value => value)
                    .ToList(),
                task.Owner,
                task.Responsible,
                task.CreatedAt,
                task.UpdatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<TaskDetailDto> GetAsync(int id, CancellationToken cancellationToken)
    {
        var task = await dbContext.TaskItems
            .AsNoTracking()
            .Include(item => item.BodyFormat)
            .Include(item => item.TaskType)
            .Include(item => item.TaskStatus)
            .Include(item => item.TaskPriority)
            .Include(item => item.TaskSource)
            .Include(item => item.WaitingTargets.Where(target => target.ResolvedAt == null))
            .Include(item => item.Tags)
                .ThenInclude(taskTag => taskTag.TaskTag)
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new ValidationException("Task was not found.", "taskId");

        return TaskDetailDto.FromTask(task);
    }

    public async Task<TaskDetailDto> CreateAsync(TaskSaveRequest request, CancellationToken cancellationToken)
    {
        var taskTypeCode = await GetDefaultTaskTypeCodeAsync(request.TaskTypeCode, cancellationToken);
        var taskPriorityCode = await GetDefaultTaskPriorityCodeAsync(request.TaskPriorityCode, cancellationToken);

        var task = await lifecycleService.CreateTaskAsync(new TaskCreateRequest(
            request.Title,
            taskTypeCode,
            request.Body,
            request.BodyFormatCode,
            taskPriorityCode,
            request.TaskSourceCode,
            request.SourceReference,
            request.SourceUrl,
            request.Deadline,
            request.Owner,
            request.Responsible), cancellationToken);

        await ApplyInitialWaitingForAsync(task.Id, request.ActiveWaitingForLabel, cancellationToken);
        await ApplyTagsAsync(task.Id, request.Tags, logChanges: false, cancellationToken);

        return await GetAsync(task.Id, cancellationToken);
    }

    public async Task<TaskDetailDto> UpdateAsync(TaskSaveRequest request, CancellationToken cancellationToken)
    {
        if (request.Id is null)
        {
            throw new ValidationException("Task id is required.", "id");
        }

        if (string.IsNullOrWhiteSpace(request.Title))
        {
            throw new ValidationException("Title is required.", "title");
        }

        if (string.IsNullOrWhiteSpace(request.TaskTypeCode))
        {
            throw new ValidationException("Task type is required.", "taskTypeCode");
        }

        var task = await dbContext.TaskItems
            .Include(item => item.BodyFormat)
            .Include(item => item.TaskType)
            .Include(item => item.TaskPriority)
            .Include(item => item.TaskSource)
            .Include(item => item.WaitingTargets.Where(target => target.ResolvedAt == null))
            .SingleOrDefaultAsync(item => item.Id == request.Id.Value, cancellationToken)
            ?? throw new ValidationException("Task was not found.", "taskId");

        var now = DateTime.UtcNow;
        var bodyFormat = string.IsNullOrWhiteSpace(request.BodyFormatCode)
            ? null
            : await GetLookupByCodeAsync(dbContext.BodyFormats, request.BodyFormatCode, cancellationToken);
        var source = string.IsNullOrWhiteSpace(request.TaskSourceCode)
            ? null
            : await GetLookupByCodeAsync(dbContext.TaskSources, request.TaskSourceCode, cancellationToken);

        var newTitle = request.Title.Trim();
        var newSourceReference = NormalizeOptional(request.SourceReference);
        var newSourceUrl = NormalizeOptional(request.SourceUrl);
        var newOwner = NormalizeOptional(request.Owner);
        var newResponsible = NormalizeOptional(request.Responsible);
        var updateLogType = await GetOrCreateTaskUpdatedLogTypeAsync(cancellationToken);

        AddFieldChangeLog(task, updateLogType, "Title", task.Title, newTitle, now);
        AddFieldChangeLog(task, updateLogType, "Source", task.TaskSource?.Name, source?.Name, now);
        AddFieldChangeLog(task, updateLogType, "Source reference", task.SourceReference, newSourceReference, now);
        AddFieldChangeLog(task, updateLogType, "Source URL", task.SourceUrl, newSourceUrl, now);
        AddFieldChangeLog(task, updateLogType, "Owner", task.Owner, newOwner, now);
        AddFieldChangeLog(task, updateLogType, "Responsible", task.Responsible, newResponsible, now);

        if (!string.Equals(task.Body, request.Body, StringComparison.Ordinal)
            || task.BodyFormatId != bodyFormat?.Id)
        {
            AddUpdateLog(task, updateLogType, "Editor changed", now);
        }

        task.Title = newTitle;
        task.Body = request.Body;
        task.BodyFormatId = bodyFormat?.Id;
        task.TaskSourceId = source?.Id;
        task.SourceReference = newSourceReference;
        task.SourceUrl = newSourceUrl;
        task.Owner = newOwner;
        task.Responsible = newResponsible;
        task.UpdatedAt = now;

        await lifecycleService.ChangeTypeAsync(task.Id, request.TaskTypeCode, cancellationToken);
        await lifecycleService.ChangePriorityAsync(task.Id, request.TaskPriorityCode, cancellationToken);
        await lifecycleService.ChangeDeadlineAsync(task.Id, request.Deadline, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
        await ApplyWaitingForAsync(task.Id, request.ActiveWaitingForLabel, cancellationToken);
        await ApplyTagsAsync(task.Id, request.Tags, logChanges: true, cancellationToken);

        return await GetAsync(task.Id, cancellationToken);
    }

    public async Task<TaskDetailDto> StartAsync(int id, CancellationToken cancellationToken)
    {
        await lifecycleService.StartTaskAsync(id, cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<TaskDetailDto> UndoStartAsync(int id, CancellationToken cancellationToken)
    {
        await lifecycleService.UndoStartTaskAsync(id, cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<TaskDetailDto> CompleteAsync(int id, CancellationToken cancellationToken)
    {
        await lifecycleService.CompleteTaskAsync(id, cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<TaskDetailDto> ReopenAsync(int id, CancellationToken cancellationToken)
    {
        await lifecycleService.ReopenTaskAsync(id, cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<TaskDetailDto> CancelAsync(int id, CancellationToken cancellationToken)
    {
        await lifecycleService.CancelTaskAsync(id, cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<TaskDetailDto> AddWaitingForAsync(
        TaskWaitingForSaveRequest request,
        CancellationToken cancellationToken)
    {
        await lifecycleService.AddWaitingForAsync(
            request.TaskId,
            new TaskWaitingForRequest(request.Label),
            cancellationToken);

        return await GetAsync(request.TaskId, cancellationToken);
    }

    public async Task<TaskDetailDto> ClearWaitingForAsync(int id, CancellationToken cancellationToken)
    {
        await lifecycleService.ClearWaitingForAsync(id, cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<IReadOnlyCollection<TaskTimelineItemDto>> GetTimelineAsync(
        TaskTimelineRequest request,
        CancellationToken cancellationToken)
    {
        await EnsureTaskExistsAsync(request.TaskId, cancellationToken);
        return await LoadTimelineAsync(request.TaskId, cancellationToken);
    }

    public async Task<IReadOnlyCollection<TaskTimelineItemDto>> AddCommentAsync(
        TaskCommentCreateRequest request,
        CancellationToken cancellationToken)
    {
        var commentText = NormalizeOptional(request.CommentText);
        if (commentText is null)
        {
            throw new ValidationException("Comment is required.", "commentText");
        }

        var task = await dbContext.TaskItems
            .SingleOrDefaultAsync(item => item.Id == request.TaskId, cancellationToken)
            ?? throw new ValidationException("Task was not found.", "taskId");

        var now = DateTime.UtcNow;
        dbContext.TaskComments.Add(new TaskComment
        {
            TaskId = task.Id,
            CommentText = commentText,
            CreatedAt = now
        });

        var commentLogType = await dbContext.TaskLogTypes
            .SingleOrDefaultAsync(logType => logType.Code == TaskLogTypeCodes.CommentAdded, cancellationToken);
        if (commentLogType is not null)
        {
            dbContext.TaskLogEntries.Add(new TaskLogEntry
            {
                TaskId = task.Id,
                TaskLogTypeId = commentLogType.Id,
                Message = "Comment added",
                CreatedAt = now
            });
        }

        task.UpdatedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);

        return await LoadTimelineAsync(task.Id, cancellationToken);
    }

    public async Task<IReadOnlyCollection<TaskTimelineItemDto>> DeleteCommentAsync(
        TaskCommentDeleteRequest request,
        CancellationToken cancellationToken)
    {
        var comment = await dbContext.TaskComments
            .SingleOrDefaultAsync(item => item.Id == request.CommentId && item.TaskId == request.TaskId, cancellationToken)
            ?? throw new ValidationException("Comment was not found.", "commentId");

        var task = await dbContext.TaskItems
            .SingleOrDefaultAsync(item => item.Id == request.TaskId, cancellationToken)
            ?? throw new ValidationException("Task was not found.", "taskId");

        task.UpdatedAt = DateTime.UtcNow;
        dbContext.TaskComments.Remove(comment);
        await dbContext.SaveChangesAsync(cancellationToken);

        return await LoadTimelineAsync(request.TaskId, cancellationToken);
    }

    private static async Task<IReadOnlyCollection<LookupItemDto>> GetLookupItemsAsync<TLookup>(
        DbSet<TLookup> dbSet,
        CancellationToken cancellationToken)
        where TLookup : LookupEntity
    {
        return await dbSet
            .AsNoTracking()
            .Where(lookup => lookup.IsActive)
            .OrderBy(lookup => lookup.SortOrder)
            .ThenBy(lookup => lookup.Name)
            .Select(lookup => new LookupItemDto(
                lookup.Code,
                lookup.Name,
                lookup.BackgroundColor,
                lookup.ForegroundColor,
                lookup.IsSelected))
            .ToListAsync(cancellationToken);
    }

    private static async Task<IReadOnlyCollection<LookupSettingsItemDto>> GetLookupSettingsItemsAsync<TLookup>(
        DbSet<TLookup> dbSet,
        IReadOnlySet<int> usedLookupIds,
        CancellationToken cancellationToken,
        bool isReadOnly = false)
        where TLookup : LookupEntity
    {
        var rows = await dbSet
            .AsNoTracking()
            .OrderBy(lookup => lookup.SortOrder)
            .ThenBy(lookup => lookup.Name)
            .Select(lookup => new
            {
                lookup.Id,
                lookup.Code,
                lookup.Name,
                lookup.Description,
                lookup.SortOrder,
                lookup.IsActive,
                lookup.IsSystem,
                lookup.IsSelected,
                lookup.BackgroundColor,
                lookup.ForegroundColor
            })
            .ToListAsync(cancellationToken);

        return rows
            .Select(lookup => new LookupSettingsItemDto(
                lookup.Code,
                lookup.Name,
                lookup.Description,
                lookup.SortOrder,
                lookup.IsActive,
                lookup.IsSystem,
                lookup.IsSelected,
                lookup.BackgroundColor,
                lookup.ForegroundColor,
                !isReadOnly && !lookup.IsSystem && !usedLookupIds.Contains(lookup.Id),
                null,
                isReadOnly))
            .ToList();
    }

    private async Task<IReadOnlyCollection<LookupSettingsItemDto>> GetRelationTypeSettingsAsync(
        IReadOnlySet<int> usedLookupIds, CancellationToken cancellationToken)
    {
        return await dbContext.TaskRelationTypes.AsNoTracking()
            .OrderBy(item => item.SortOrder).ThenBy(item => item.Name)
            .Select(item => new LookupSettingsItemDto(
                item.Code, item.Name, item.Description, item.SortOrder, item.IsActive, item.IsSystem,
                item.IsSelected, item.BackgroundColor, item.ForegroundColor,
                !item.IsSystem && !usedLookupIds.Contains(item.Id), item.ReverseName, false))
            .ToListAsync(cancellationToken);
    }

    private async Task UpdateTaskStatusLookupAsync(
        LookupUpdateRequest request,
        CancellationToken cancellationToken)
    {
        var code = NormalizeLookupCode(request.Code);
        if ((code == TaskStatusCodes.Active || code == TaskStatusCodes.Completed || code == TaskStatusCodes.Cancelled)
            && !request.IsActive)
        {
            throw new ValidationException("Required task statuses cannot be deactivated.", "isActive");
        }

        await UpdateLookupAsync(dbContext.TaskStatuses, request, cancellationToken);
    }

    private static async Task CreateLookupAsync<TLookup>(
        DbSet<TLookup> dbSet,
        LookupCreateRequest request,
        CancellationToken cancellationToken)
        where TLookup : LookupEntity, new()
    {
        ValidateLookupName(request.Name);
        var code = NormalizeNewLookupCode(request.Code);

        if (await dbSet.AnyAsync(lookup => lookup.Code == code, cancellationToken))
        {
            throw new ValidationException("Lookup code already exists.", "code");
        }

        var now = DateTime.UtcNow;

        dbSet.Add(new TLookup
        {
            Code = code,
            Name = request.Name.Trim(),
            Description = NormalizeOptional(request.Description),
            SortOrder = request.SortOrder,
            IsActive = request.IsActive,
            IsSelected = request.IsSelected,
            BackgroundColor = NormalizeColor(request.BackgroundColor, nameof(request.BackgroundColor)),
            ForegroundColor = NormalizeColor(request.ForegroundColor, nameof(request.ForegroundColor)),
            IsSystem = false,
            CreatedAt = now,
            UpdatedAt = now
        });
    }

    private static async Task UpdateLookupAsync<TLookup>(
        DbSet<TLookup> dbSet,
        LookupUpdateRequest request,
        CancellationToken cancellationToken)
        where TLookup : LookupEntity
    {
        ValidateLookupName(request.Name);

        var lookup = await dbSet.SingleOrDefaultAsync(
            item => item.Code == NormalizeLookupCode(request.Code),
            cancellationToken)
            ?? throw new ValidationException("Lookup item was not found.", "code");
        var now = DateTime.UtcNow;

        lookup.Name = request.Name.Trim();
        lookup.Description = NormalizeOptional(request.Description);
        lookup.SortOrder = request.SortOrder;
        lookup.IsActive = request.IsActive;
        lookup.IsSelected = request.IsSelected;
        lookup.BackgroundColor = NormalizeColor(request.BackgroundColor, nameof(request.BackgroundColor));
        lookup.ForegroundColor = NormalizeColor(request.ForegroundColor, nameof(request.ForegroundColor));
        lookup.UpdatedAt = now;
    }

    private static async Task DeleteLookupAsync<TLookup>(
        DbSet<TLookup> dbSet,
        string code,
        Func<int, CancellationToken, Task<bool>> isUsedAsync,
        CancellationToken cancellationToken)
        where TLookup : LookupEntity
    {
        var lookup = await dbSet.SingleOrDefaultAsync(
            item => item.Code == NormalizeLookupCode(code),
            cancellationToken)
            ?? throw new ValidationException("Lookup item was not found.", "code");

        if (lookup.IsSystem)
        {
            throw new ValidationException("System lookup values cannot be deleted.", "code");
        }

        if (await isUsedAsync(lookup.Id, cancellationToken))
        {
            throw new ValidationException("Lookup value is used by a task and cannot be deleted.", "code");
        }

        dbSet.Remove(lookup);
    }

    private static async Task ReorderLookupAsync<TLookup>(
        DbSet<TLookup> dbSet,
        LookupReorderRequest request,
        CancellationToken cancellationToken)
        where TLookup : LookupEntity
    {
        var orderedCodes = request.OrderedCodes
            .Select(NormalizeLookupCode)
            .ToList();

        if (orderedCodes.Count == 0)
        {
            throw new ValidationException("Lookup order is required.", "orderedCodes");
        }

        if (orderedCodes.Distinct(StringComparer.Ordinal).Count() != orderedCodes.Count)
        {
            throw new ValidationException("Lookup order contains duplicate codes.", "orderedCodes");
        }

        var lookups = await dbSet.ToListAsync(cancellationToken);
        var lookupCodes = lookups
            .Select(lookup => lookup.Code)
            .OrderBy(code => code, StringComparer.Ordinal)
            .ToList();
        var sortedOrderedCodes = orderedCodes
            .OrderBy(code => code, StringComparer.Ordinal)
            .ToList();

        if (!lookupCodes.SequenceEqual(sortedOrderedCodes, StringComparer.Ordinal))
        {
            throw new ValidationException("Lookup order must include every lookup code exactly once.", "orderedCodes");
        }

        var now = DateTime.UtcNow;
        var lookupsByCode = lookups.ToDictionary(lookup => lookup.Code, StringComparer.Ordinal);
        for (var index = 0; index < orderedCodes.Count; index++)
        {
            var lookup = lookupsByCode[orderedCodes[index]];
            lookup.SortOrder = (index + 1) * 10;
            lookup.UpdatedAt = now;
        }
    }

    private static async Task<TLookup> GetLookupByCodeAsync<TLookup>(
        DbSet<TLookup> dbSet,
        string code,
        CancellationToken cancellationToken)
        where TLookup : LookupEntity
    {
        var normalizedCode = code.Trim().ToUpperInvariant();

        return await dbSet.SingleOrDefaultAsync(lookup => lookup.Code == normalizedCode, cancellationToken)
            ?? throw new ValidationException($"Lookup code '{normalizedCode}' was not found.", typeof(TLookup).Name);
    }

    private async Task<string> GetDefaultTaskTypeCodeAsync(string? requestedCode, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(requestedCode))
        {
            return requestedCode;
        }

        var selectedCode = await dbContext.TaskTypes
            .AsNoTracking()
            .Where(type => type.IsActive)
            .OrderByDescending(type => type.IsSelected)
            .ThenBy(type => type.SortOrder)
            .ThenBy(type => type.Name)
            .Select(type => type.Code)
            .FirstOrDefaultAsync(cancellationToken);

        return selectedCode ?? throw new ValidationException("Task type is required.", "taskTypeCode");
    }

    private async Task<string?> GetDefaultTaskPriorityCodeAsync(string? requestedCode, CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(requestedCode))
        {
            return requestedCode;
        }

        return await dbContext.TaskPriorities
            .AsNoTracking()
            .Where(priority => priority.IsActive && priority.IsSelected)
            .OrderBy(priority => priority.SortOrder)
            .ThenBy(priority => priority.Name)
            .Select(priority => priority.Code)
            .FirstOrDefaultAsync(cancellationToken);
    }

    private async Task ApplyInitialWaitingForAsync(
        int taskId,
        string? requestedLabel,
        CancellationToken cancellationToken)
    {
        var normalizedLabel = NormalizeOptional(requestedLabel);
        if (normalizedLabel is null)
        {
            return;
        }

        var task = await dbContext.TaskItems
            .SingleAsync(item => item.Id == taskId, cancellationToken);
        var now = task.CreatedAt;

        dbContext.TaskWaitingFors.Add(new TaskWaitingFor
        {
            TaskId = task.Id,
            Label = normalizedLabel,
            WaitingSince = now,
            CreatedAt = now,
            UpdatedAt = now
        });
        task.WaitingSince = now;

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task ApplyWaitingForAsync(int taskId, string? requestedLabel, CancellationToken cancellationToken)
    {
        var normalizedLabel = NormalizeOptional(requestedLabel);
        var activeWaitingFor = await dbContext.TaskWaitingFors
            .AsNoTracking()
            .SingleOrDefaultAsync(target => target.TaskId == taskId && target.ResolvedAt == null, cancellationToken);

        if (activeWaitingFor is null && normalizedLabel is null)
        {
            return;
        }

        if (activeWaitingFor is not null && string.Equals(activeWaitingFor.Label, normalizedLabel, StringComparison.Ordinal))
        {
            return;
        }

        if (normalizedLabel is null)
        {
            await lifecycleService.ClearWaitingForAsync(taskId, cancellationToken);
            return;
        }

        await lifecycleService.AddWaitingForAsync(
            taskId,
            new TaskWaitingForRequest(normalizedLabel),
            cancellationToken);
    }

    private async Task ApplyTagsAsync(
        int taskId,
        IReadOnlyCollection<string>? requestedValues,
        bool logChanges,
        CancellationToken cancellationToken)
    {
        var values = (requestedValues ?? [])
            .Select(NormalizeOptional)
            .Where(value => value is not null)
            .Select(value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var task = await dbContext.TaskItems
            .Include(item => item.Tags)
                .ThenInclude(taskTag => taskTag.TaskTag)
            .SingleAsync(item => item.Id == taskId, cancellationToken);

        var requested = values.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var oldValues = task.Tags
            .Where(taskTag => taskTag.TaskTag is not null)
            .Select(taskTag => taskTag.TaskTag!.Value)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var removed = task.Tags
            .Where(taskTag => taskTag.TaskTag is not null && !requested.Contains(taskTag.TaskTag.Value))
            .ToList();
        dbContext.TaskTaskTags.RemoveRange(removed);

        var current = task.Tags
            .Where(taskTag => !removed.Contains(taskTag) && taskTag.TaskTag is not null)
            .Select(taskTag => taskTag.TaskTag!.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var value in values.Where(value => !current.Contains(value)))
        {
            var tag = await dbContext.TaskTags
                .SingleOrDefaultAsync(existing => existing.Value == value, cancellationToken);

            if (tag is null)
            {
                tag = new TaskTag { Value = value };
                dbContext.TaskTags.Add(tag);
            }

            task.Tags.Add(new TaskTaskTag { TaskTag = tag });
        }

        var newValues = values.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList();
        if (logChanges && !oldValues.SequenceEqual(newValues, StringComparer.OrdinalIgnoreCase))
        {
            var updateLogType = await GetOrCreateTaskUpdatedLogTypeAsync(cancellationToken);
            AddFieldChangeLog(
                task,
                updateLogType,
                "Tags",
                oldValues.Count == 0 ? null : string.Join(", ", oldValues),
                newValues.Count == 0 ? null : string.Join(", ", newValues),
                DateTime.UtcNow);
        }

        task.UpdatedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<TaskLogType> GetOrCreateTaskUpdatedLogTypeAsync(CancellationToken cancellationToken)
    {
        var logType = await dbContext.TaskLogTypes
            .SingleOrDefaultAsync(item => item.Code == TaskLogTypeCodes.TaskUpdated, cancellationToken);
        if (logType is not null)
        {
            return logType;
        }

        var now = DateTime.UtcNow;
        logType = new TaskLogType
        {
            Code = TaskLogTypeCodes.TaskUpdated,
            Name = "Task updated",
            SortOrder = 230,
            IsActive = true,
            IsSystem = true,
            CreatedAt = now,
            UpdatedAt = now
        };
        dbContext.TaskLogTypes.Add(logType);
        return logType;
    }

    private static void AddFieldChangeLog(
        TaskItem task,
        TaskLogType logType,
        string fieldName,
        string? oldValue,
        string? newValue,
        DateTime now)
    {
        if (string.Equals(oldValue, newValue, StringComparison.Ordinal))
        {
            return;
        }

        AddUpdateLog(
            task,
            logType,
            $"{fieldName}: Changed '{DisplayLogValue(oldValue)}' to '{DisplayLogValue(newValue)}'",
            now,
            oldValue,
            newValue);
    }

    private static void AddUpdateLog(
        TaskItem task,
        TaskLogType logType,
        string message,
        DateTime now,
        string? oldValue = null,
        string? newValue = null)
    {
        task.LogEntries.Add(new TaskLogEntry
        {
            TaskLogType = logType,
            Message = message,
            OldValue = oldValue,
            NewValue = newValue,
            CreatedAt = now
        });
    }

    private static string DisplayLogValue(string? value) => value ?? "(none)";

    private static string NormalizeLookupCode(string code)
    {
        return string.IsNullOrWhiteSpace(code)
            ? throw new ValidationException("Lookup code is required.", "code")
            : code.Trim().ToUpperInvariant();
    }

    private static string NormalizeNewLookupCode(string code)
    {
        var normalized = NormalizeLookupCode(code);
        return System.Text.RegularExpressions.Regex.IsMatch(normalized, "^[A-Z0-9_]+$")
            ? normalized
            : throw new ValidationException("Lookup code can only contain letters, numbers, and underscores.", "code");
    }

    private static void ValidateLookupName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ValidationException("Name is required.", "name");
        }
    }

    private static string? NormalizeColor(string? value, string field)
    {
        var normalized = NormalizeOptional(value);
        if (normalized is null)
        {
            return null;
        }

        if (System.Text.RegularExpressions.Regex.IsMatch(normalized, "^#[0-9a-fA-F]{6}$"))
        {
            return normalized.ToLowerInvariant();
        }

        throw new ValidationException("Color must be a hex value.", field);
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string NormalizeTagValue(string? value)
    {
        return NormalizeOptional(value)
            ?? throw new ValidationException("Tag value is required.", "value");
    }

    private async Task EnsureTaskExistsAsync(int taskId, CancellationToken cancellationToken)
    {
        if (!await dbContext.TaskItems.AnyAsync(task => task.Id == taskId, cancellationToken))
        {
            throw new ValidationException("Task was not found.", "taskId");
        }
    }

    private async Task<IReadOnlyCollection<TaskTimelineItemDto>> LoadTimelineAsync(
        int taskId,
        CancellationToken cancellationToken)
    {
        var comments = await dbContext.TaskComments
            .AsNoTracking()
            .Where(comment => comment.TaskId == taskId)
            .Select(comment => new TaskTimelineItemDto(
                "comment",
                comment.Id,
                comment.CommentText,
                null,
                null,
                null,
                comment.CreatedAt,
                true))
            .ToListAsync(cancellationToken);

        var logs = await dbContext.TaskLogEntries
            .AsNoTracking()
            .Include(log => log.TaskLogType)
            .Where(log => log.TaskId == taskId)
            .Select(log => new TaskTimelineItemDto(
                "log",
                log.Id,
                log.Message,
                log.TaskLogType == null ? null : log.TaskLogType.Code,
                log.OldValue,
                log.NewValue,
                log.CreatedAt,
                false))
            .ToListAsync(cancellationToken);

        return comments
            .Concat(logs)
            .OrderByDescending(item => item.CreatedAt)
            .ThenBy(item => item.Kind)
            .ThenByDescending(item => item.Id)
            .ToList();
    }
}

public sealed record TaskLookupsDto(
    IReadOnlyCollection<LookupItemDto> TaskTypes,
    IReadOnlyCollection<LookupItemDto> TaskPriorities,
    IReadOnlyCollection<LookupItemDto> TaskSources,
    IReadOnlyCollection<LookupItemDto> BodyFormats,
    IReadOnlyCollection<string> Tags);

public sealed record LookupItemDto(
    string Code,
    string Name,
    string? BackgroundColor,
    string? ForegroundColor,
    bool IsSelected);

public sealed record TaskLookupSettingsDto(
    IReadOnlyCollection<LookupSettingsItemDto> TaskTypes,
    IReadOnlyCollection<LookupSettingsItemDto> TaskPriorities,
    IReadOnlyCollection<LookupSettingsItemDto> TaskStatuses,
    IReadOnlyCollection<LookupSettingsItemDto> TaskSources,
    IReadOnlyCollection<LookupSettingsItemDto> TaskRelationTypes,
    IReadOnlyCollection<LookupSettingsItemDto> BodyFormats,
    IReadOnlyCollection<LookupSettingsItemDto> TaskLogTypes);

public sealed record LookupSettingsItemDto(
    string Code,
    string Name,
    string? Description,
    int SortOrder,
    bool IsActive,
    bool IsSystem,
    bool IsSelected,
    string? BackgroundColor,
    string? ForegroundColor,
    bool CanDelete,
    string? ReverseName,
    bool IsReadOnly);

public sealed record LookupUpdateRequest(
    string Group,
    string Code,
    string Name,
    string? Description,
    int SortOrder,
    bool IsActive,
    bool IsSelected,
    string? BackgroundColor,
    string? ForegroundColor,
    string? ReverseName = null);

public sealed record LookupCreateRequest(
    string Group,
    string Code,
    string Name,
    string? Description,
    int SortOrder,
    bool IsActive,
    bool IsSelected,
    string? BackgroundColor,
    string? ForegroundColor,
    string? ReverseName = null);

public sealed record LookupDeleteRequest(
    string Group,
    string Code);

public sealed record LookupReorderRequest(
    string Group,
    IReadOnlyList<string> OrderedCodes);

public sealed record TaskListRequest(string? View);

public sealed record TagSettingsItemDto(int Id, string Value, int UsageCount);

public sealed record TagRenameRequest(int TagId, string Value);

public sealed record TagDeleteRequest(int TagId);

public sealed record TagMergeRequest(int SourceTagId, int TargetTagId);

public sealed record TaskGetRequest(int Id);

public sealed record TaskIdRequest(int Id);

public sealed record TaskWaitingForSaveRequest(
    int TaskId,
    string? Label);

public sealed record TaskTimelineRequest(int TaskId);

public sealed record TaskCommentCreateRequest(
    int TaskId,
    string CommentText);

public sealed record TaskCommentDeleteRequest(
    int TaskId,
    int CommentId);

public sealed record TaskSaveRequest(
    int? Id,
    string Title,
    string TaskTypeCode,
    string? Body,
    string? BodyFormatCode,
    string? TaskPriorityCode,
    string? TaskSourceCode,
    string? SourceReference,
    string? SourceUrl,
    DateTime? Deadline,
    string? ActiveWaitingForLabel = null,
    IReadOnlyCollection<string>? Tags = null,
    string? Owner = null,
    string? Responsible = null);

public sealed record TaskListItemDto(
    int Id,
    string Title,
    string TaskTypeCode,
    string TaskTypeName,
    int TaskTypeSortOrder,
    string? TaskTypeBackgroundColor,
    string? TaskTypeForegroundColor,
    string TaskStatusCode,
    string TaskStatusName,
    int TaskStatusSortOrder,
    string? TaskStatusBackgroundColor,
    string? TaskStatusForegroundColor,
    string? TaskPriorityCode,
    string? TaskPriorityName,
    int? TaskPrioritySortOrder,
    string? TaskPriorityBackgroundColor,
    string? TaskPriorityForegroundColor,
    DateTime? Deadline,
    string? ActiveWaitingForLabel,
    DateTime? WaitingSince,
    int CompletedChecklistCount,
    int ChecklistCount,
    IReadOnlyCollection<string> Tags,
    string? Owner,
    string? Responsible,
    DateTime CreatedAt,
    DateTime UpdatedAt);

public sealed record TaskDetailDto(
    int Id,
    string Title,
    string? Body,
    string? BodyFormatCode,
    string TaskTypeCode,
    string TaskTypeName,
    string TaskStatusCode,
    string TaskStatusName,
    string? TaskPriorityCode,
    string? TaskPriorityName,
    string? TaskSourceCode,
    string? TaskSourceName,
    string? SourceReference,
    string? SourceUrl,
    string? Owner,
    string? Responsible,
    DateTime? Deadline,
    IReadOnlyCollection<string> Tags,
    TaskWaitingForDto? ActiveWaitingFor,
    DateTime CreatedAt,
    DateTime UpdatedAt)
{
    public static TaskDetailDto FromTask(TaskItem task)
    {
        return new TaskDetailDto(
            task.Id,
            task.Title,
            task.Body,
            task.BodyFormat?.Code,
            task.TaskType?.Code ?? string.Empty,
            task.TaskType?.Name ?? string.Empty,
            task.TaskStatus?.Code ?? string.Empty,
            task.TaskStatus?.Name ?? string.Empty,
            task.TaskPriority?.Code,
            task.TaskPriority?.Name,
            task.TaskSource?.Code,
            task.TaskSource?.Name,
            task.SourceReference,
            task.SourceUrl,
            task.Owner,
            task.Responsible,
            task.Deadline,
            task.Tags
                .Where(taskTag => taskTag.TaskTag is not null)
                .Select(taskTag => taskTag.TaskTag!.Value)
                .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                .ToList(),
            task.WaitingTargets
                .Where(target => target.ResolvedAt is null)
                .Select(TaskWaitingForDto.FromWaitingFor)
                .SingleOrDefault(),
            task.CreatedAt,
            task.UpdatedAt);
    }
}

public sealed record TaskWaitingForDto(
    int Id,
    string? Label,
    DateTime WaitingSince)
{
    public static TaskWaitingForDto FromWaitingFor(TaskWaitingFor waitingFor)
    {
        return new TaskWaitingForDto(
            waitingFor.Id,
            waitingFor.Label,
            waitingFor.WaitingSince);
    }
}

public sealed record TaskTimelineItemDto(
    string Kind,
    int Id,
    string Text,
    string? LogTypeCode,
    string? OldValue,
    string? NewValue,
    DateTime CreatedAt,
    bool CanDelete);
