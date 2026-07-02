using Microsoft.EntityFrameworkCore;
using Photino.Okf_Todo.Data;

namespace Photino.Okf_Todo.Services;

public sealed class ImageService(AppDbContext dbContext)
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

        var imageData = Convert.FromBase64String(request.Base64Data);

        if (imageData.Length > MaxImageBytes)
        {
            throw new BridgeException("ImageTooLarge", "Image must be 5 MB or smaller.");
        }

        var issueExists = await dbContext.Issues.AnyAsync(issue => issue.Id == request.IssueId, cancellationToken);
        if (!issueExists)
        {
            throw new BridgeException("NotFound", "Issue was not found.");
        }

        var image = new ImageAsset
        {
            IssueId = request.IssueId,
            Filename = request.Filename,
            MimeType = request.MimeType,
            Width = request.Width,
            Height = request.Height,
            ImageData = imageData,
            CreatedUtc = DateTime.UtcNow
        };

        dbContext.Images.Add(image);
        await dbContext.SaveChangesAsync(cancellationToken);

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
    int IssueId,
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
