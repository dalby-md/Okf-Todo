using Microsoft.EntityFrameworkCore;
using Photino.Okf_Todo.Data;

namespace Photino.Okf_Todo.Services;

public sealed class TaskChecklistService(AppDbContext dbContext)
{
    public async Task<IReadOnlyCollection<TaskChecklistItemDto>> ListAsync(
        int taskId,
        CancellationToken cancellationToken)
    {
        await EnsureTaskExistsAsync(taskId, cancellationToken);
        return await LoadAsync(taskId, cancellationToken);
    }

    public async Task<IReadOnlyCollection<TaskChecklistItemDto>> CreateAsync(
        TaskChecklistCreateRequest request,
        CancellationToken cancellationToken)
    {
        var text = NormalizeText(request.Text);
        var task = await GetTaskAsync(request.TaskId, cancellationToken);
        var now = DateTime.UtcNow;
        var nextSortOrder = (await dbContext.TaskChecklistItems
            .Where(item => item.TaskId == task.Id)
            .MaxAsync(item => (int?)item.SortOrder, cancellationToken) ?? 0) + 10;

        dbContext.TaskChecklistItems.Add(new TaskChecklistItem
        {
            TaskId = task.Id,
            Text = text,
            SortOrder = nextSortOrder,
            CreatedAt = now,
            UpdatedAt = now
        });
        await AddLogAsync(task.Id, "CHECKLIST_ITEM_ADDED", $"Checklist item added: {text}", null, text, now, cancellationToken);
        task.UpdatedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);
        return await LoadAsync(task.Id, cancellationToken);
    }

    public async Task<IReadOnlyCollection<TaskChecklistItemDto>> UpdateAsync(
        TaskChecklistUpdateRequest request,
        CancellationToken cancellationToken)
    {
        var text = NormalizeText(request.Text);
        var item = await GetItemAsync(request.TaskId, request.ChecklistItemId, cancellationToken);
        if (string.Equals(item.Text, text, StringComparison.Ordinal))
        {
            return await LoadAsync(request.TaskId, cancellationToken);
        }

        item.Text = text;
        item.UpdatedAt = DateTime.UtcNow;
        await TouchTaskAsync(request.TaskId, item.UpdatedAt, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return await LoadAsync(request.TaskId, cancellationToken);
    }

    public async Task<IReadOnlyCollection<TaskChecklistItemDto>> SetCompletedAsync(
        TaskChecklistCompleteRequest request,
        CancellationToken cancellationToken)
    {
        var item = await GetItemAsync(request.TaskId, request.ChecklistItemId, cancellationToken);
        if (item.IsCompleted == request.IsCompleted)
        {
            return await LoadAsync(request.TaskId, cancellationToken);
        }

        var now = DateTime.UtcNow;
        item.IsCompleted = request.IsCompleted;
        item.CompletedAt = request.IsCompleted ? now : null;
        item.UpdatedAt = now;
        await TouchTaskAsync(request.TaskId, now, cancellationToken);
        await AddLogAsync(
            request.TaskId,
            request.IsCompleted ? "CHECKLIST_ITEM_COMPLETED" : "CHECKLIST_ITEM_REOPENED",
            request.IsCompleted ? $"Checklist item completed: {item.Text}" : $"Checklist item reopened: {item.Text}",
            request.IsCompleted ? null : item.Text,
            request.IsCompleted ? item.Text : null,
            now,
            cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return await LoadAsync(request.TaskId, cancellationToken);
    }

    public async Task<IReadOnlyCollection<TaskChecklistItemDto>> ReorderAsync(
        TaskChecklistReorderRequest request,
        CancellationToken cancellationToken)
    {
        var items = await dbContext.TaskChecklistItems
            .Where(item => item.TaskId == request.TaskId)
            .ToListAsync(cancellationToken);
        var requestedIds = request.OrderedChecklistItemIds.ToList();
        if (requestedIds.Count != items.Count
            || requestedIds.Distinct().Count() != requestedIds.Count
            || requestedIds.Any(id => items.All(item => item.Id != id)))
        {
            throw new ValidationException("Checklist order must contain every checklist item exactly once.", "orderedChecklistItemIds");
        }

        var now = DateTime.UtcNow;
        for (var index = 0; index < requestedIds.Count; index++)
        {
            var item = items.Single(candidate => candidate.Id == requestedIds[index]);
            item.SortOrder = (index + 1) * 10;
            item.UpdatedAt = now;
        }

        await TouchTaskAsync(request.TaskId, now, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return await LoadAsync(request.TaskId, cancellationToken);
    }

    public async Task<IReadOnlyCollection<TaskChecklistItemDto>> DeleteAsync(
        TaskChecklistDeleteRequest request,
        CancellationToken cancellationToken)
    {
        var item = await GetItemAsync(request.TaskId, request.ChecklistItemId, cancellationToken);
        dbContext.TaskChecklistItems.Remove(item);
        var now = DateTime.UtcNow;
        await TouchTaskAsync(request.TaskId, now, cancellationToken);
        await dbContext.SaveChangesAsync(cancellationToken);
        return await LoadAsync(request.TaskId, cancellationToken);
    }

    private async Task<IReadOnlyCollection<TaskChecklistItemDto>> LoadAsync(int taskId, CancellationToken cancellationToken)
    {
        return await dbContext.TaskChecklistItems
            .AsNoTracking()
            .Where(item => item.TaskId == taskId)
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.Id)
            .Select(item => new TaskChecklistItemDto(
                item.Id,
                item.Text,
                item.SortOrder,
                item.IsCompleted,
                item.CompletedAt,
                item.CreatedAt,
                item.UpdatedAt))
            .ToListAsync(cancellationToken);
    }

    private async Task<TaskItem> GetTaskAsync(int taskId, CancellationToken cancellationToken)
    {
        return await dbContext.TaskItems.SingleOrDefaultAsync(task => task.Id == taskId, cancellationToken)
            ?? throw new ValidationException("Task was not found.", "taskId");
    }

    private async Task<TaskChecklistItem> GetItemAsync(int taskId, int checklistItemId, CancellationToken cancellationToken)
    {
        return await dbContext.TaskChecklistItems.SingleOrDefaultAsync(
            item => item.Id == checklistItemId && item.TaskId == taskId,
            cancellationToken)
            ?? throw new ValidationException("Checklist item was not found.", "checklistItemId");
    }

    private async Task EnsureTaskExistsAsync(int taskId, CancellationToken cancellationToken)
    {
        if (!await dbContext.TaskItems.AnyAsync(task => task.Id == taskId, cancellationToken))
        {
            throw new ValidationException("Task was not found.", "taskId");
        }
    }

    private async Task TouchTaskAsync(int taskId, DateTime now, CancellationToken cancellationToken)
    {
        var task = await GetTaskAsync(taskId, cancellationToken);
        task.UpdatedAt = now;
    }

    private async Task AddLogAsync(
        int taskId,
        string code,
        string message,
        string? oldValue,
        string? newValue,
        DateTime now,
        CancellationToken cancellationToken)
    {
        var logType = await dbContext.TaskLogTypes.SingleAsync(item => item.Code == code, cancellationToken);
        dbContext.TaskLogEntries.Add(new TaskLogEntry
        {
            TaskId = taskId,
            TaskLogTypeId = logType.Id,
            Message = message,
            OldValue = oldValue,
            NewValue = newValue,
            CreatedAt = now
        });
    }

    private static string NormalizeText(string text)
    {
        return string.IsNullOrWhiteSpace(text)
            ? throw new ValidationException("Checklist text is required.", "text")
            : text.Trim();
    }
}

public sealed record TaskChecklistCreateRequest(int TaskId, string Text);
public sealed record TaskChecklistUpdateRequest(int TaskId, int ChecklistItemId, string Text);
public sealed record TaskChecklistCompleteRequest(int TaskId, int ChecklistItemId, bool IsCompleted);
public sealed record TaskChecklistReorderRequest(int TaskId, IReadOnlyCollection<int> OrderedChecklistItemIds);
public sealed record TaskChecklistDeleteRequest(int TaskId, int ChecklistItemId);
public sealed record TaskChecklistListRequest(int TaskId);
public sealed record TaskChecklistItemDto(
    int Id,
    string Text,
    int SortOrder,
    bool IsCompleted,
    DateTime? CompletedAt,
    DateTime CreatedAt,
    DateTime UpdatedAt);
