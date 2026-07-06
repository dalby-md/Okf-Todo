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
            "inbox" => query.Where(task => task.TaskStatus != null && task.TaskStatus.Code == TaskStatusCodes.New),
            "active" => query.Where(task => task.TaskStatus != null
                && (task.TaskStatus.Code == TaskStatusCodes.New || task.TaskStatus.Code == TaskStatusCodes.Active)),
            "completed" => query.Where(task => task.TaskStatus != null
                && (task.TaskStatus.Code == TaskStatusCodes.Completed || task.TaskStatus.Code == TaskStatusCodes.Cancelled)),
            "all" => query,
            _ => query.Where(task => task.TaskStatus != null
                && (task.TaskStatus.Code == TaskStatusCodes.New || task.TaskStatus.Code == TaskStatusCodes.Active))
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
                task.TaskStatus!.Code,
                task.TaskStatus.Name,
                task.TaskPriority == null ? null : task.TaskPriority.Name,
                task.Deadline,
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
            .SingleOrDefaultAsync(item => item.Id == id, cancellationToken)
            ?? throw new ValidationException("Task was not found.", "taskId");

        return TaskDetailDto.FromTask(task);
    }

    public async Task<TaskDetailDto> CreateAsync(TaskSaveRequest request, CancellationToken cancellationToken)
    {
        var task = await lifecycleService.CreateTaskAsync(new TaskCreateRequest(
            request.Title,
            request.TaskTypeCode,
            request.Body,
            request.BodyFormatCode,
            request.TaskPriorityCode,
            request.TaskSourceCode,
            request.SourceReference,
            request.SourceUrl,
            request.Deadline), cancellationToken);

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

        return await GetAsync(task.Id, cancellationToken);
    }

    public async Task<TaskDetailDto> StartAsync(int id, CancellationToken cancellationToken)
    {
        await lifecycleService.StartTaskAsync(id, cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<TaskDetailDto> CompleteAsync(int id, CancellationToken cancellationToken)
    {
        await lifecycleService.CompleteTaskAsync(id, cancellationToken);
        return await GetAsync(id, cancellationToken);
    }

    public async Task<TaskDetailDto> CancelAsync(int id, CancellationToken cancellationToken)
    {
        await lifecycleService.CancelTaskAsync(id, cancellationToken);
        return await GetAsync(id, cancellationToken);
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
            .Select(lookup => new LookupItemDto(lookup.Code, lookup.Name))
            .ToListAsync(cancellationToken);
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

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}

public sealed record TaskLookupsDto(
    IReadOnlyCollection<LookupItemDto> TaskTypes,
    IReadOnlyCollection<LookupItemDto> TaskPriorities,
    IReadOnlyCollection<LookupItemDto> TaskSources,
    IReadOnlyCollection<LookupItemDto> BodyFormats);

public sealed record LookupItemDto(string Code, string Name);

public sealed record TaskListRequest(string? View);

public sealed record TaskGetRequest(int Id);

public sealed record TaskIdRequest(int Id);

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
    DateTime? Deadline);

public sealed record TaskListItemDto(
    int Id,
    string Title,
    string TaskTypeName,
    string TaskStatusCode,
    string TaskStatusName,
    string? TaskPriorityName,
    DateTime? Deadline,
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
            task.CreatedAt,
            task.UpdatedAt);
    }
}
