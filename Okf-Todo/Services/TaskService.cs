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
            await GetLookupItemsAsync(dbContext.BodyFormats, cancellationToken));
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

        return new TaskLookupSettingsDto(
            await GetLookupSettingsItemsAsync(dbContext.TaskTypes, usedTaskTypeIds, cancellationToken),
            await GetLookupSettingsItemsAsync(dbContext.TaskPriorities, usedTaskPriorityIds, cancellationToken),
            await GetLookupSettingsItemsAsync(dbContext.TaskStatuses, usedTaskStatusIds, cancellationToken));
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
                throw new ValidationException("Lookup group is not supported.", "group");
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
                throw new ValidationException("Lookup group is not supported.", "group");
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
                throw new ValidationException("Lookup group is not supported.", "group");
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
                throw new ValidationException("Lookup group is not supported.", "group");
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return await GetLookupSettingsAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<TaskListItemDto>> ListAsync(
        TaskListRequest request,
        CancellationToken cancellationToken)
    {
        var view = string.IsNullOrWhiteSpace(request.View) ? "active" : request.View.Trim().ToLowerInvariant();
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
            "completed" => query.Where(task => task.TaskStatus != null
                && (task.TaskStatus.Code == TaskStatusCodes.Completed || task.TaskStatus.Code == TaskStatusCodes.Cancelled)),
            "all" => query,
            _ => query.Where(task => task.TaskStatus != null
                && task.TaskStatus.Code == TaskStatusCodes.Active)
        };

        return await query
            .OrderBy(task => task.TaskStatus!.SortOrder)
            .ThenBy(task => task.Deadline == null)
            .ThenBy(task => task.Deadline)
            .ThenByDescending(task => task.UpdatedAt)
            .Select(task => new TaskListItemDto(
                task.Id,
                task.Title,
                task.TaskType!.Name,
                task.TaskType.BackgroundColor,
                task.TaskType.ForegroundColor,
                task.TaskStatus!.Code,
                task.TaskStatus.Name,
                task.TaskStatus.BackgroundColor,
                task.TaskStatus.ForegroundColor,
                task.TaskPriority == null ? null : task.TaskPriority.Name,
                task.TaskPriority == null ? null : task.TaskPriority.BackgroundColor,
                task.TaskPriority == null ? null : task.TaskPriority.ForegroundColor,
                task.Deadline,
                task.WaitingTargets
                    .Where(waitingFor => waitingFor.ResolvedAt == null)
                    .Select(waitingFor => waitingFor.Label)
                    .SingleOrDefault(),
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
            request.Deadline), cancellationToken);

        await ApplyWaitingForAsync(task.Id, request.ActiveWaitingForLabel, cancellationToken);

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

        task.Title = request.Title.Trim();
        task.Body = request.Body;
        task.BodyFormatId = bodyFormat?.Id;
        task.TaskSourceId = source?.Id;
        task.SourceReference = NormalizeOptional(request.SourceReference);
        task.SourceUrl = NormalizeOptional(request.SourceUrl);
        task.UpdatedAt = now;

        await lifecycleService.ChangeTypeAsync(task.Id, request.TaskTypeCode, cancellationToken);
        await lifecycleService.ChangePriorityAsync(task.Id, request.TaskPriorityCode, cancellationToken);
        await lifecycleService.ChangeDeadlineAsync(task.Id, request.Deadline, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
        await ApplyWaitingForAsync(task.Id, request.ActiveWaitingForLabel, cancellationToken);

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
        CancellationToken cancellationToken)
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
                !lookup.IsSystem && !usedLookupIds.Contains(lookup.Id)))
            .ToList();
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
    IReadOnlyCollection<LookupItemDto> BodyFormats);

public sealed record LookupItemDto(
    string Code,
    string Name,
    string? BackgroundColor,
    string? ForegroundColor,
    bool IsSelected);

public sealed record TaskLookupSettingsDto(
    IReadOnlyCollection<LookupSettingsItemDto> TaskTypes,
    IReadOnlyCollection<LookupSettingsItemDto> TaskPriorities,
    IReadOnlyCollection<LookupSettingsItemDto> TaskStatuses);

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
    bool CanDelete);

public sealed record LookupUpdateRequest(
    string Group,
    string Code,
    string Name,
    string? Description,
    int SortOrder,
    bool IsActive,
    bool IsSelected,
    string? BackgroundColor,
    string? ForegroundColor);

public sealed record LookupCreateRequest(
    string Group,
    string Code,
    string Name,
    string? Description,
    int SortOrder,
    bool IsActive,
    bool IsSelected,
    string? BackgroundColor,
    string? ForegroundColor);

public sealed record LookupDeleteRequest(
    string Group,
    string Code);

public sealed record LookupReorderRequest(
    string Group,
    IReadOnlyList<string> OrderedCodes);

public sealed record TaskListRequest(string? View);

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
    string? ActiveWaitingForLabel = null);

public sealed record TaskListItemDto(
    int Id,
    string Title,
    string TaskTypeName,
    string? TaskTypeBackgroundColor,
    string? TaskTypeForegroundColor,
    string TaskStatusCode,
    string TaskStatusName,
    string? TaskStatusBackgroundColor,
    string? TaskStatusForegroundColor,
    string? TaskPriorityName,
    string? TaskPriorityBackgroundColor,
    string? TaskPriorityForegroundColor,
    DateTime? Deadline,
    string? ActiveWaitingForLabel,
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
    DateTime? Deadline,
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
            task.Deadline,
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
    DateTime CreatedAt,
    bool CanDelete);
