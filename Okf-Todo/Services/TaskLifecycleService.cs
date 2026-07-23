using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Photino.Okf_Todo.Data;

namespace Photino.Okf_Todo.Services;

public sealed class TaskLifecycleService(
    AppDbContext dbContext,
    ILogger<TaskLifecycleService> logger)
{
    public async Task<TaskItem> CreateTaskAsync(TaskCreateRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            throw new ValidationException("Title is required.", "title");
        }

        if (string.IsNullOrWhiteSpace(request.TaskTypeCode))
        {
            throw new ValidationException("Task type is required.", "taskTypeCode");
        }

        var now = DateTime.UtcNow;
        var taskType = await GetLookupByCodeAsync(dbContext.TaskTypes, request.TaskTypeCode, cancellationToken);
        var status = await GetLookupByCodeAsync(dbContext.TaskStatuses, TaskStatusCodes.Active, cancellationToken);
        var priority = string.IsNullOrWhiteSpace(request.TaskPriorityCode)
            ? null
            : await GetLookupByCodeAsync(dbContext.TaskPriorities, request.TaskPriorityCode, cancellationToken);
        var source = string.IsNullOrWhiteSpace(request.TaskSourceCode)
            ? null
            : await GetLookupByCodeAsync(dbContext.TaskSources, request.TaskSourceCode, cancellationToken);
        var bodyFormat = string.IsNullOrWhiteSpace(request.BodyFormatCode)
            ? null
            : await GetLookupByCodeAsync(dbContext.BodyFormats, request.BodyFormatCode, cancellationToken);

        var task = new TaskItem
        {
            Title = request.Title.Trim(),
            Body = request.Body,
            BodyFormatId = bodyFormat?.Id,
            TaskTypeId = taskType.Id,
            TaskStatusId = status.Id,
            TaskPriorityId = priority?.Id,
            TaskSourceId = source?.Id,
            SourceReference = NormalizeOptional(request.SourceReference),
            SourceUrl = NormalizeOptional(request.SourceUrl),
            Owner = NormalizeOptional(request.Owner),
            Responsible = NormalizeOptional(request.Responsible),
            Deadline = request.Deadline,
            CreatedAt = now,
            UpdatedAt = now,
            ActivatedAt = now
        };

        dbContext.TaskItems.Add(task);
        AddLog(task, await GetLogTypeAsync(TaskLogTypeCodes.TaskCreated, cancellationToken), "Task created", now);

        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Created task {TaskId} with status {StatusCode}.", task.Id, TaskStatusCodes.Active);

        return task;
    }

    public async Task StartTaskAsync(int taskId, CancellationToken cancellationToken = default)
    {
        var task = await GetTaskWithStatusAsync(taskId, cancellationToken);
        var now = DateTime.UtcNow;

        if (task.ActivatedAt is null)
        {
            task.ActivatedAt = now;
        }

        await ChangeStatusAsync(task, TaskStatusCodes.Active, now, cancellationToken);
        task.UpdatedAt = now;

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UndoStartTaskAsync(int taskId, CancellationToken cancellationToken = default)
    {
        _ = await GetTaskWithStatusAsync(taskId, cancellationToken);
        throw new ValidationException("Start cannot be undone because tasks are active by default.", "taskStatus");
    }

    public async Task AddWaitingForAsync(
        int taskId,
        TaskWaitingForRequest request,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(request.Label))
        {
            throw new ValidationException("Waiting for is required.", "waitingFor");
        }

        var task = await GetTaskWithStatusAsync(taskId, cancellationToken);
        if (task.TaskStatus?.Code != TaskStatusCodes.Active)
        {
            throw new ValidationException("Waiting for can only be set on active tasks.", "taskStatus");
        }

        var now = DateTime.UtcNow;
        var label = request.Label.Trim();
        var waitingFor = await dbContext.TaskWaitingFors
            .SingleOrDefaultAsync(target => target.TaskId == taskId && target.ResolvedAt == null, cancellationToken);

        if (waitingFor is null)
        {
            waitingFor = new TaskWaitingFor
            {
                TaskId = task.Id,
                Label = label,
                WaitingSince = now,
                CreatedAt = now,
                UpdatedAt = now
            };

            dbContext.TaskWaitingFors.Add(waitingFor);
            task.WaitingSince = now;
        }
        else
        {
            waitingFor.Label = label;
            waitingFor.UpdatedAt = now;
        }

        task.UpdatedAt = now;

        var targetText = waitingFor.Label!;
        AddLog(
            task,
            await GetLogTypeAsync(TaskLogTypeCodes.WaitingForChanged, cancellationToken),
            $"Waiting for changed to {targetText}",
            now,
            newValue: targetText);

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task ClearWaitingForAsync(int taskId, CancellationToken cancellationToken = default)
    {
        var task = await GetTaskWithStatusAsync(taskId, cancellationToken);
        var waitingFor = await dbContext.TaskWaitingFors
            .SingleOrDefaultAsync(target => target.TaskId == taskId && target.ResolvedAt == null, cancellationToken);

        if (waitingFor is null)
        {
            throw new ValidationException("Task does not have an active waiting target.", "waitingFor");
        }

        var now = DateTime.UtcNow;
        waitingFor.ResolvedAt = now;
        waitingFor.UpdatedAt = now;
        task.WaitingSince = null;
        task.UpdatedAt = now;

        var targetText = waitingFor.Label ?? string.Empty;
        AddLog(
            task,
            await GetLogTypeAsync(TaskLogTypeCodes.WaitingForCleared, cancellationToken),
            $"Waiting for {targetText} was cleared",
            now,
            oldValue: targetText);

        await ChangeStatusAsync(task, TaskStatusCodes.Active, now, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task CompleteTaskAsync(int taskId, CancellationToken cancellationToken = default)
    {
        var task = await GetTaskWithStatusAsync(taskId, cancellationToken);
        var now = DateTime.UtcNow;

        await ResolveActiveWaitingTargetAsync(task, now, cancellationToken);
        task.CompletedAt = now;
        task.CancelledAt = null;
        task.WaitingSince = null;
        task.UpdatedAt = now;

        await ChangeStatusAsync(task, TaskStatusCodes.Completed, now, cancellationToken);
        AddLog(task, await GetLogTypeAsync(TaskLogTypeCodes.TaskCompleted, cancellationToken), "Task completed", now);

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task ReopenTaskAsync(int taskId, CancellationToken cancellationToken = default)
    {
        var task = await GetTaskWithStatusAsync(taskId, cancellationToken);
        var now = DateTime.UtcNow;

        task.CompletedAt = null;
        task.CancelledAt = null;
        task.UpdatedAt = now;

        await ChangeStatusAsync(task, TaskStatusCodes.Active, now, cancellationToken);
        AddLog(task, await GetLogTypeAsync(TaskLogTypeCodes.TaskReopened, cancellationToken), "Task reopened", now);

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task CancelTaskAsync(int taskId, CancellationToken cancellationToken = default)
    {
        var task = await GetTaskWithStatusAsync(taskId, cancellationToken);
        var now = DateTime.UtcNow;

        await ResolveActiveWaitingTargetAsync(task, now, cancellationToken);
        task.CancelledAt = now;
        task.CompletedAt = null;
        task.WaitingSince = null;
        task.UpdatedAt = now;

        await ChangeStatusAsync(task, TaskStatusCodes.Cancelled, now, cancellationToken);
        AddLog(task, await GetLogTypeAsync(TaskLogTypeCodes.TaskCancelled, cancellationToken), "Task cancelled", now);

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task ChangePriorityAsync(
        int taskId,
        string? taskPriorityCode,
        CancellationToken cancellationToken = default)
    {
        var task = await dbContext.TaskItems
            .Include(item => item.TaskPriority)
            .SingleOrDefaultAsync(item => item.Id == taskId, cancellationToken)
            ?? throw new ValidationException("Task was not found.", "taskId");

        var now = DateTime.UtcNow;
        var oldValue = task.TaskPriority?.Name;
        var priority = string.IsNullOrWhiteSpace(taskPriorityCode)
            ? null
            : await GetLookupByCodeAsync(dbContext.TaskPriorities, taskPriorityCode, cancellationToken);

        if (task.TaskPriorityId == priority?.Id)
        {
            return;
        }

        task.TaskPriorityId = priority?.Id;
        task.UpdatedAt = now;

        AddLog(
            task,
            await GetLogTypeAsync(TaskLogTypeCodes.PriorityChanged, cancellationToken),
            $"Priority changed from {oldValue ?? "(none)"} to {priority?.Name ?? "(none)"}",
            now,
            oldValue,
            priority?.Name);

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task ChangeTypeAsync(int taskId, string taskTypeCode, CancellationToken cancellationToken = default)
    {
        var task = await dbContext.TaskItems
            .Include(item => item.TaskType)
            .SingleOrDefaultAsync(item => item.Id == taskId, cancellationToken)
            ?? throw new ValidationException("Task was not found.", "taskId");

        var now = DateTime.UtcNow;
        var taskType = await GetLookupByCodeAsync(dbContext.TaskTypes, taskTypeCode, cancellationToken);
        if (task.TaskTypeId == taskType.Id)
        {
            return;
        }

        var oldValue = task.TaskType?.Name;
        task.TaskTypeId = taskType.Id;
        task.UpdatedAt = now;

        AddLog(
            task,
            await GetLogTypeAsync(TaskLogTypeCodes.TypeChanged, cancellationToken),
            $"Type changed from {oldValue ?? "(none)"} to {taskType.Name}",
            now,
            oldValue,
            taskType.Name);

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task ChangeDeadlineAsync(int taskId, DateTime? deadline, CancellationToken cancellationToken = default)
    {
        var task = await dbContext.TaskItems.FindAsync([taskId], cancellationToken)
            ?? throw new ValidationException("Task was not found.", "taskId");

        if (task.Deadline == deadline)
        {
            return;
        }

        var now = DateTime.UtcNow;
        var oldValue = task.Deadline?.ToString("O");
        var newValue = deadline?.ToString("O");

        task.Deadline = deadline;
        task.UpdatedAt = now;

        AddLog(
            task,
            await GetLogTypeAsync(TaskLogTypeCodes.DeadlineChanged, cancellationToken),
            $"Deadline changed from {oldValue ?? "(none)"} to {newValue ?? "(none)"}",
            now,
            oldValue,
            newValue);

        await dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task ChangeStatusAsync(
        TaskItem task,
        string newStatusCode,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var newStatus = await GetLookupByCodeAsync(dbContext.TaskStatuses, newStatusCode, cancellationToken);
        if (task.TaskStatusId == newStatus.Id)
        {
            return;
        }

        var oldStatusName = task.TaskStatus?.Name
            ?? await dbContext.TaskStatuses
                .Where(status => status.Id == task.TaskStatusId)
                .Select(status => status.Name)
                .SingleAsync(cancellationToken);

        task.TaskStatusId = newStatus.Id;
        task.TaskStatus = newStatus;

        AddLog(
            task,
            await GetLogTypeAsync(TaskLogTypeCodes.StatusChanged, cancellationToken),
            $"Status changed from {oldStatusName} to {newStatus.Name}",
            now,
            oldStatusName,
            newStatus.Name);
    }

    private async Task ResolveActiveWaitingTargetAsync(
        TaskItem task,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var waitingFor = await dbContext.TaskWaitingFors
            .SingleOrDefaultAsync(target => target.TaskId == task.Id && target.ResolvedAt == null, cancellationToken);

        if (waitingFor is null)
        {
            return;
        }

        waitingFor.ResolvedAt = now;
        waitingFor.UpdatedAt = now;

        var targetText = waitingFor.Label ?? string.Empty;
        AddLog(
            task,
            await GetLogTypeAsync(TaskLogTypeCodes.WaitingForCleared, cancellationToken),
            $"Waiting for {targetText} was cleared",
            now,
            oldValue: targetText);
    }

    private async Task<TaskItem> GetTaskWithStatusAsync(int taskId, CancellationToken cancellationToken)
    {
        return await dbContext.TaskItems
            .Include(task => task.TaskStatus)
            .SingleOrDefaultAsync(task => task.Id == taskId, cancellationToken)
            ?? throw new ValidationException("Task was not found.", "taskId");
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

    private async Task<TaskLogType> GetLogTypeAsync(string code, CancellationToken cancellationToken)
    {
        return await GetLookupByCodeAsync(dbContext.TaskLogTypes, code, cancellationToken);
    }

    private static void AddLog(
        TaskItem task,
        TaskLogType logType,
        string message,
        DateTime now,
        string? oldValue = null,
        string? newValue = null)
    {
        task.LogEntries.Add(new TaskLogEntry
        {
            TaskLogTypeId = logType.Id,
            Message = message,
            OldValue = oldValue,
            NewValue = newValue,
            CreatedAt = now
        });
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}

public sealed record TaskCreateRequest(
    string Title,
    string TaskTypeCode,
    string? Body = null,
    string? BodyFormatCode = null,
    string? TaskPriorityCode = null,
    string? TaskSourceCode = null,
    string? SourceReference = null,
    string? SourceUrl = null,
    DateTime? Deadline = null,
    string? Owner = null,
    string? Responsible = null);

public sealed record TaskWaitingForRequest(string? Label);

public static class TaskStatusCodes
{
    public const string Active = "ACTIVE";
    public const string Completed = "COMPLETED";
    public const string Cancelled = "CANCELLED";
}

public static class TaskPriorityCodes
{
    public const string Urgent = "URGENT";
    public const string CanWait = "CAN_WAIT";
}

public static class TaskLogTypeCodes
{
    public const string TaskCreated = "TASK_CREATED";
    public const string StatusChanged = "STATUS_CHANGED";
    public const string TypeChanged = "TYPE_CHANGED";
    public const string PriorityChanged = "PRIORITY_CHANGED";
    public const string DeadlineChanged = "DEADLINE_CHANGED";
    public const string WaitingForChanged = "WAITING_FOR_CHANGED";
    public const string WaitingForCleared = "WAITING_FOR_CLEARED";
    public const string TaskCompleted = "TASK_COMPLETED";
    public const string TaskReopened = "TASK_REOPENED";
    public const string TaskCancelled = "TASK_CANCELLED";
    public const string CommentAdded = "COMMENT_ADDED";
    public const string TaskUpdated = "TASK_UPDATED";
}
