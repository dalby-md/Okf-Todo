namespace Photino.Okf_Todo.Data;

public sealed class Issue
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Status { get; set; } = "Open";

    public int Priority { get; set; }

    public DateTime? DueUtc { get; set; }

    public DateTime CreatedUtc { get; set; }

    public DateTime ModifiedUtc { get; set; }

    public string BodyHtml { get; set; } = string.Empty;

    public string BodyMarkdown { get; set; } = string.Empty;

    public string EditorMode { get; set; } = "html";

    public List<ImageAsset> Images { get; set; } = [];
}
