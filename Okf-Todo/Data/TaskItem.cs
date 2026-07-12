namespace Photino.Okf_Todo.Data;

public sealed class TaskItem
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Body { get; set; }

    public int? BodyFormatId { get; set; }

    public BodyFormat? BodyFormat { get; set; }

    public int TaskTypeId { get; set; }

    public TaskType? TaskType { get; set; }

    public int TaskStatusId { get; set; }

    public TaskStatus? TaskStatus { get; set; }

    public int? TaskPriorityId { get; set; }

    public TaskPriority? TaskPriority { get; set; }

    public int? TaskSourceId { get; set; }

    public TaskSource? TaskSource { get; set; }

    public string? SourceReference { get; set; }

    public string? SourceUrl { get; set; }

    public DateTime? Deadline { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public DateTime? ActivatedAt { get; set; }

    public DateTime? WaitingSince { get; set; }

    public DateTime? CompletedAt { get; set; }

    public DateTime? CancelledAt { get; set; }

    public List<TaskWaitingFor> WaitingTargets { get; set; } = [];

    public List<TaskComment> Comments { get; set; } = [];

    public List<TaskLogEntry> LogEntries { get; set; } = [];

    public List<TaskChecklistItem> ChecklistItems { get; set; } = [];

    public List<TaskAttachment> Attachments { get; set; } = [];

    public List<ImageAsset> Images { get; set; } = [];

    public List<TaskTaskTag> Tags { get; set; } = [];

    public List<TaskRelation> SourceRelations { get; set; } = [];

    public List<TaskRelation> TargetRelations { get; set; } = [];
}
