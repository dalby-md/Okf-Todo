namespace Photino.Okf_Todo.Data;

public abstract class LookupEntity
{
    public int Id { get; set; }

    public string Code { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public string? BackgroundColor { get; set; }

    public string? ForegroundColor { get; set; }

    public bool IsSelected { get; set; }

    public int SortOrder { get; set; }

    public bool IsActive { get; set; } = true;

    public bool IsSystem { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}

public sealed class TaskType : LookupEntity
{
    public List<TaskItem> Tasks { get; set; } = [];
}

public sealed class TaskStatus : LookupEntity
{
    public List<TaskItem> Tasks { get; set; } = [];
}

public sealed class TaskPriority : LookupEntity
{
    public List<TaskItem> Tasks { get; set; } = [];
}

public sealed class TaskSource : LookupEntity
{
    public List<TaskItem> Tasks { get; set; } = [];
}

public sealed class TaskLogType : LookupEntity
{
    public List<TaskLogEntry> LogEntries { get; set; } = [];
}

public sealed class BodyFormat : LookupEntity
{
    public List<TaskItem> Tasks { get; set; } = [];
}
