namespace Photino.Okf_Todo.Data;

public sealed class TaskAttachment
{
    public int Id { get; set; }

    public int TaskId { get; set; }

    public TaskItem? Task { get; set; }

    public string FileName { get; set; } = string.Empty;

    public string? ContentType { get; set; }

    public long FileSize { get; set; }

    public string? Sha256Hash { get; set; }

    public byte[] ContentBlob { get; set; } = [];

    public string? Description { get; set; }

    public DateTime CreatedAt { get; set; }
}
