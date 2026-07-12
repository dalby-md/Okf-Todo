namespace Photino.Okf_Todo.Data;

public sealed class TaskTag
{
    public int Id { get; set; }

    public string Value { get; set; } = string.Empty;

    public List<TaskTaskTag> Tasks { get; set; } = [];
}

public sealed class TaskTaskTag
{
    public int TaskId { get; set; }

    public TaskItem? Task { get; set; }

    public int TaskTagId { get; set; }

    public TaskTag? TaskTag { get; set; }
}
