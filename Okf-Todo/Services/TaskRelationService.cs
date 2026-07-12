using Microsoft.EntityFrameworkCore;
using Photino.Okf_Todo.Data;

namespace Photino.Okf_Todo.Services;

public sealed class TaskRelationService(AppDbContext dbContext)
{
    public async Task<TaskRelationOptionsDto> GetOptionsAsync(int taskId, CancellationToken token)
    {
        await EnsureTaskAsync(taskId, token);
        return new TaskRelationOptionsDto(
            await dbContext.TaskRelationTypes.AsNoTracking().Where(type => type.IsActive)
                .OrderBy(type => type.SortOrder)
                .Select(type => new TaskRelationTypeDto(type.Code, type.Name, type.ReverseName))
                .ToListAsync(token),
            await dbContext.TaskItems.AsNoTracking().Where(task => task.Id != taskId)
                .OrderBy(task => task.Title)
                .Select(task => new RelatedTaskOptionDto(task.Id, task.Title))
                .ToListAsync(token));
    }

    public async Task<IReadOnlyCollection<TaskRelationDto>> ListAsync(int taskId, CancellationToken token)
    {
        await EnsureTaskAsync(taskId, token);
        return await LoadAsync(taskId, token);
    }

    public async Task<IReadOnlyCollection<TaskRelationDto>> CreateAsync(TaskRelationCreateRequest request, CancellationToken token)
    {
        if (request.TaskId == request.TargetTaskId)
            throw new ValidationException("A task cannot be related to itself.", "targetTaskId");

        var source = await dbContext.TaskItems.SingleOrDefaultAsync(task => task.Id == request.TaskId, token)
            ?? throw new ValidationException("Task was not found.", "taskId");
        var target = await dbContext.TaskItems.SingleOrDefaultAsync(task => task.Id == request.TargetTaskId, token)
            ?? throw new ValidationException("Related task was not found.", "targetTaskId");
        var code = request.RelationTypeCode.Trim().ToUpperInvariant();
        var type = await dbContext.TaskRelationTypes.SingleOrDefaultAsync(item => item.Code == code && item.IsActive, token)
            ?? throw new ValidationException("Relationship type was not found.", "relationTypeCode");

        var duplicate = await dbContext.TaskRelations.AnyAsync(relation =>
            relation.TaskRelationTypeId == type.Id
            && ((relation.SourceTaskId == source.Id && relation.TargetTaskId == target.Id)
                || (relation.SourceTaskId == target.Id && relation.TargetTaskId == source.Id)), token);
        if (duplicate)
            throw new ValidationException("This relationship already exists.", "targetTaskId");

        var now = DateTime.UtcNow;
        dbContext.TaskRelations.Add(new TaskRelation
        {
            SourceTaskId = source.Id,
            TargetTaskId = target.Id,
            TaskRelationTypeId = type.Id,
            CreatedAt = now
        });
        await AddLogAsync(source.Id, "RELATION_ADDED", $"Relationship added: {type.Name} {target.Title}", target.Title, now, token);
        await AddLogAsync(target.Id, "RELATION_ADDED", $"Relationship added: {type.ReverseName} {source.Title}", source.Title, now, token);
        source.UpdatedAt = now;
        target.UpdatedAt = now;
        await dbContext.SaveChangesAsync(token);
        return await LoadAsync(source.Id, token);
    }

    public async Task<IReadOnlyCollection<TaskRelationDto>> DeleteAsync(TaskRelationDeleteRequest request, CancellationToken token)
    {
        var relation = await dbContext.TaskRelations
            .Include(item => item.SourceTask).Include(item => item.TargetTask).Include(item => item.TaskRelationType)
            .SingleOrDefaultAsync(item => item.Id == request.RelationId
                && (item.SourceTaskId == request.TaskId || item.TargetTaskId == request.TaskId), token)
            ?? throw new ValidationException("Relationship was not found.", "relationId");
        var now = DateTime.UtcNow;
        var source = relation.SourceTask!;
        var target = relation.TargetTask!;
        var type = relation.TaskRelationType!;
        dbContext.TaskRelations.Remove(relation);
        await AddLogAsync(source.Id, "RELATION_REMOVED", $"Relationship removed: {type.Name} {target.Title}", target.Title, now, token);
        await AddLogAsync(target.Id, "RELATION_REMOVED", $"Relationship removed: {type.ReverseName} {source.Title}", source.Title, now, token);
        source.UpdatedAt = now;
        target.UpdatedAt = now;
        await dbContext.SaveChangesAsync(token);
        return await LoadAsync(request.TaskId, token);
    }

    private async Task<IReadOnlyCollection<TaskRelationDto>> LoadAsync(int taskId, CancellationToken token)
    {
        var rows = await dbContext.TaskRelations.AsNoTracking()
            .Include(item => item.SourceTask).Include(item => item.TargetTask).Include(item => item.TaskRelationType)
            .Where(item => item.SourceTaskId == taskId || item.TargetTaskId == taskId)
            .OrderBy(item => item.CreatedAt).ToListAsync(token);
        return rows.Select(item => item.SourceTaskId == taskId
            ? new TaskRelationDto(item.Id, item.TargetTaskId, item.TargetTask!.Title, item.TaskRelationType!.Name, item.TaskRelationType.Code)
            : new TaskRelationDto(item.Id, item.SourceTaskId, item.SourceTask!.Title, item.TaskRelationType!.ReverseName, item.TaskRelationType.Code))
            .ToList();
    }

    private async Task EnsureTaskAsync(int taskId, CancellationToken token)
    {
        if (!await dbContext.TaskItems.AnyAsync(task => task.Id == taskId, token))
            throw new ValidationException("Task was not found.", "taskId");
    }

    private async Task AddLogAsync(int taskId, string code, string message, string value, DateTime now, CancellationToken token)
    {
        var type = await dbContext.TaskLogTypes.SingleAsync(item => item.Code == code, token);
        dbContext.TaskLogEntries.Add(new TaskLogEntry
        {
            TaskId = taskId,
            TaskLogTypeId = type.Id,
            Message = message,
            OldValue = code == "RELATION_REMOVED" ? value : null,
            NewValue = code == "RELATION_ADDED" ? value : null,
            CreatedAt = now
        });
    }
}

public sealed record TaskRelationOptionsRequest(int TaskId);
public sealed record TaskRelationListRequest(int TaskId);
public sealed record TaskRelationCreateRequest(int TaskId, int TargetTaskId, string RelationTypeCode);
public sealed record TaskRelationDeleteRequest(int TaskId, int RelationId);
public sealed record TaskRelationTypeDto(string Code, string Name, string ReverseName);
public sealed record RelatedTaskOptionDto(int Id, string Title);
public sealed record TaskRelationOptionsDto(IReadOnlyCollection<TaskRelationTypeDto> RelationTypes, IReadOnlyCollection<RelatedTaskOptionDto> Tasks);
public sealed record TaskRelationDto(int Id, int RelatedTaskId, string RelatedTaskTitle, string RelationName, string RelationTypeCode);
