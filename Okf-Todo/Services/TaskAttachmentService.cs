using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Photino.Okf_Todo.Data;

namespace Photino.Okf_Todo.Services;

public sealed class TaskAttachmentService(AppDbContext dbContext)
{
    private const int MaximumFileSize = 25 * 1024 * 1024;

    public async Task<IReadOnlyCollection<TaskAttachmentDto>> ListAsync(int taskId, CancellationToken cancellationToken)
    {
        await EnsureTaskExistsAsync(taskId, cancellationToken);
        return await dbContext.TaskAttachments
            .AsNoTracking()
            .Where(attachment => attachment.TaskId == taskId)
            .OrderByDescending(attachment => attachment.CreatedAt)
            .Select(attachment => new TaskAttachmentDto(
                attachment.Id,
                attachment.FileName,
                attachment.ContentType,
                attachment.FileSize,
                attachment.Description,
                attachment.CreatedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyCollection<TaskAttachmentDto>> CreateAsync(
        TaskAttachmentCreateRequest request,
        CancellationToken cancellationToken)
    {
        var task = await dbContext.TaskItems.SingleOrDefaultAsync(task => task.Id == request.TaskId, cancellationToken)
            ?? throw new ValidationException("Task was not found.", "taskId");
        var fileName = Path.GetFileName(request.FileName?.Trim());
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ValidationException("File name is required.", "fileName");
        }

        byte[] content;
        try
        {
            content = Convert.FromBase64String(request.Base64Data);
        }
        catch (FormatException)
        {
            throw new ValidationException("File content is invalid.", "base64Data");
        }

        if (content.Length > MaximumFileSize)
        {
            throw new ValidationException("Attachments cannot exceed 25 MB.", "base64Data");
        }

        var now = DateTime.UtcNow;
        dbContext.TaskAttachments.Add(new TaskAttachment
        {
            TaskId = task.Id,
            FileName = fileName,
            ContentType = NormalizeOptional(request.ContentType),
            FileSize = content.LongLength,
            Sha256Hash = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant(),
            ContentBlob = content,
            Description = NormalizeOptional(request.Description),
            CreatedAt = now
        });
        await AddLogAsync(task.Id, "ATTACHMENT_ADDED", $"Attachment added: {fileName}", fileName, now, cancellationToken);
        task.UpdatedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);
        return await ListAsync(task.Id, cancellationToken);
    }

    public async Task<TaskAttachmentContentDto> GetAsync(int attachmentId, CancellationToken cancellationToken)
    {
        var attachment = await dbContext.TaskAttachments.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == attachmentId, cancellationToken)
            ?? throw new ValidationException("Attachment was not found.", "attachmentId");
        return new TaskAttachmentContentDto(
            attachment.FileName,
            attachment.ContentType ?? "application/octet-stream",
            Convert.ToBase64String(attachment.ContentBlob));
    }

    public async Task<IReadOnlyCollection<TaskAttachmentDto>> DeleteAsync(
        TaskAttachmentDeleteRequest request,
        CancellationToken cancellationToken)
    {
        var attachment = await dbContext.TaskAttachments
            .SingleOrDefaultAsync(item => item.Id == request.AttachmentId && item.TaskId == request.TaskId, cancellationToken)
            ?? throw new ValidationException("Attachment was not found.", "attachmentId");
        var task = await dbContext.TaskItems.SingleAsync(item => item.Id == request.TaskId, cancellationToken);
        var now = DateTime.UtcNow;
        dbContext.TaskAttachments.Remove(attachment);
        await AddLogAsync(task.Id, "ATTACHMENT_REMOVED", $"Attachment removed: {attachment.FileName}", attachment.FileName, now, cancellationToken);
        task.UpdatedAt = now;
        await dbContext.SaveChangesAsync(cancellationToken);
        return await ListAsync(task.Id, cancellationToken);
    }

    private async Task AddLogAsync(int taskId, string code, string message, string value, DateTime now, CancellationToken token)
    {
        var type = await dbContext.TaskLogTypes.SingleAsync(item => item.Code == code, token);
        dbContext.TaskLogEntries.Add(new TaskLogEntry
        {
            TaskId = taskId,
            TaskLogTypeId = type.Id,
            Message = message,
            NewValue = code == "ATTACHMENT_ADDED" ? value : null,
            OldValue = code == "ATTACHMENT_REMOVED" ? value : null,
            CreatedAt = now
        });
    }

    private async Task EnsureTaskExistsAsync(int taskId, CancellationToken token)
    {
        if (!await dbContext.TaskItems.AnyAsync(task => task.Id == taskId, token))
        {
            throw new ValidationException("Task was not found.", "taskId");
        }
    }

    private static string? NormalizeOptional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public sealed record TaskAttachmentCreateRequest(
    int TaskId,
    string FileName,
    string? ContentType,
    string Base64Data,
    string? Description);

public sealed record TaskAttachmentDeleteRequest(int TaskId, int AttachmentId);
public sealed record TaskAttachmentGetRequest(int AttachmentId);
public sealed record TaskAttachmentListRequest(int TaskId);
public sealed record TaskAttachmentDto(int Id, string FileName, string? ContentType, long FileSize, string? Description, DateTime CreatedAt);
public sealed record TaskAttachmentContentDto(string FileName, string ContentType, string Base64Data);
