namespace Photino.Okf_Todo.Data;

public sealed class ImageAsset
{
    public int Id { get; set; }

    public int IssueId { get; set; }

    public Issue? Issue { get; set; }

    public string? Filename { get; set; }

    public string MimeType { get; set; } = string.Empty;

    public int? Width { get; set; }

    public int? Height { get; set; }

    public byte[] ImageData { get; set; } = [];

    public DateTime CreatedUtc { get; set; }
}
