using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Photino.Okf_Todo.Data;

namespace Photino.Okf_Todo.Services;

public sealed class ImageService(AppDbContext dbContext, ILogger<ImageService> logger)
{
    private const int MaxImageBytes = 5 * 1024 * 1024;
    private static readonly HashSet<string> AllowedMimeTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png",
        "image/jpeg",
        "image/gif",
        "image/webp"
    };

    public async Task<ImageCreateResult> CreateAsync(ImageCreateRequest request, CancellationToken cancellationToken)
    {
        if (!AllowedMimeTypes.Contains(request.MimeType))
        {
            throw new BridgeException("UnsupportedImageType", "Only PNG, JPEG, GIF, and WebP images are supported.");
        }

        var hasIssueOwner = request.IssueId is not null;
        var hasTaskOwner = request.TaskId is not null;
        if (hasIssueOwner == hasTaskOwner)
        {
            throw new BridgeException("ValidationFailed", "Image must belong to exactly one issue or task.");
        }

        byte[] imageData;
        try
        {
            imageData = Convert.FromBase64String(request.Base64Data);
        }
        catch (FormatException)
        {
            throw new BridgeException("ValidationFailed", "Image data is not valid base64.");
        }

        if (imageData.Length > MaxImageBytes)
        {
            throw new BridgeException("ImageTooLarge", "Image must be 5 MB or smaller.");
        }

        if (request.IssueId is not null)
        {
            var issueExists = await dbContext.Issues.AnyAsync(issue => issue.Id == request.IssueId, cancellationToken);
            if (!issueExists)
            {
                throw new BridgeException("NotFound", "Issue was not found.");
            }
        }

        if (request.TaskId is not null)
        {
            var taskExists = await dbContext.TaskItems.AnyAsync(task => task.Id == request.TaskId, cancellationToken);
            if (!taskExists)
            {
                throw new BridgeException("NotFound", "Task was not found.");
            }
        }

        var image = new ImageAsset
        {
            IssueId = request.IssueId,
            TaskId = request.TaskId,
            Filename = request.Filename,
            MimeType = request.MimeType,
            Width = request.Width,
            Height = request.Height,
            ImageData = imageData,
            CreatedUtc = DateTime.UtcNow
        };

        dbContext.Images.Add(image);
        await dbContext.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Stored editor image {ImageId} for {OwnerType} {OwnerId}.",
            image.Id,
            request.TaskId is null ? "issue" : "task",
            request.TaskId ?? request.IssueId);

        return new ImageCreateResult(image.Id, $"app://image/{image.Id}");
    }

    public async Task<ImageDto> GetAsync(int id, CancellationToken cancellationToken)
    {
        var image = await dbContext.Images.FindAsync([id], cancellationToken);

        if (image is null)
        {
            throw new BridgeException("NotFound", "Image was not found.");
        }

        return new ImageDto(
            image.Id,
            image.MimeType,
            Convert.ToBase64String(image.ImageData),
            image.Filename,
            image.Width,
            image.Height);
    }
}

public sealed record ImageCreateRequest(
    int? IssueId,
    int? TaskId,
    string? Filename,
    string MimeType,
    string Base64Data,
    int? Width,
    int? Height);

public sealed record ImageCreateResult(int Id, string Src);

public sealed record ImageDto(
    int Id,
    string MimeType,
    string Base64Data,
    string? Filename,
    int? Width,
    int? Height);

public sealed class BridgeException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
