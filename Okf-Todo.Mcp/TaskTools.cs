using System.ComponentModel;
using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Photino.Okf_Todo.Services;

namespace OkfTodo.Mcp;

[McpServerToolType]
public static class TaskTools
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    [McpServerTool(Name = "task_list", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("List OKF-Todo tasks. The view can be inbox, active, waiting, completed, or all.")]
    public static Task<IReadOnlyCollection<TaskListItemDto>> ListAsync(
        ApplicationCommandService commandService,
        [Description("Task view: inbox, active, waiting, completed, or all. Defaults to active.")] string? view = null,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync<IReadOnlyCollection<TaskListItemDto>>(
            commandService,
            "task.list",
            new TaskListRequest(view),
            cancellationToken);

    [McpServerTool(Name = "task_get", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Get one OKF-Todo task by its numeric ID.")]
    public static Task<TaskDetailDto> GetAsync(
        ApplicationCommandService commandService,
        [Description("Numeric task ID.")] int id,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync<TaskDetailDto>(
            commandService,
            "task.get",
            new TaskGetRequest(id),
            cancellationToken);

    [McpServerTool(Name = "task_create", ReadOnly = false, Destructive = false, Idempotent = false, OpenWorld = false)]
    [Description("Create an OKF-Todo task and return the saved task. Lookup inputs use stable codes, not display names.")]
    public static Task<TaskDetailDto> CreateAsync(
        ApplicationCommandService commandService,
        [Description("Task title.")] string title,
        [Description("Stable task type code, for example TASK or BUG.")] string taskTypeCode,
        [Description("Optional HTML task body.")] string? body = null,
        [Description("Stable body format code. Defaults to HTML.")] string? bodyFormatCode = "HTML",
        [Description("Optional stable priority code.")] string? taskPriorityCode = null,
        [Description("Optional stable source code.")] string? taskSourceCode = null,
        [Description("Optional source reference.")] string? sourceReference = null,
        [Description("Optional source URL.")] string? sourceUrl = null,
        [Description("Optional deadline in ISO 8601 form.")] DateTime? deadline = null,
        [Description("Optional waiting-for label. Supplying it places the task in waiting state.")] string? activeWaitingForLabel = null,
        [Description("Optional plain-string tags.")] IReadOnlyCollection<string>? tags = null,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync<TaskDetailDto>(
            commandService,
            "task.create",
            new TaskSaveRequest(
                null,
                title,
                taskTypeCode,
                body,
                bodyFormatCode,
                taskPriorityCode,
                taskSourceCode,
                sourceReference,
                sourceUrl,
                deadline,
                activeWaitingForLabel,
                tags),
            cancellationToken);

    [McpServerTool(Name = "task_update", ReadOnly = false, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Replace the editable fields of an existing OKF-Todo task. Call task_get first and pass every value that must be preserved; omitted optional fields are cleared.")]
    public static Task<TaskDetailDto> UpdateAsync(
        ApplicationCommandService commandService,
        [Description("Numeric task ID.")] int id,
        [Description("Replacement task title.")] string title,
        [Description("Replacement stable task type code.")] string taskTypeCode,
        [Description("Replacement HTML body; null clears it.")] string? body = null,
        [Description("Replacement stable body format code; null clears it.")] string? bodyFormatCode = null,
        [Description("Replacement stable priority code; null clears it.")] string? taskPriorityCode = null,
        [Description("Replacement stable source code; null clears it.")] string? taskSourceCode = null,
        [Description("Replacement source reference; null clears it.")] string? sourceReference = null,
        [Description("Replacement source URL; null clears it.")] string? sourceUrl = null,
        [Description("Replacement deadline; null clears it.")] DateTime? deadline = null,
        [Description("Replacement waiting-for label; null clears active waiting.")] string? activeWaitingForLabel = null,
        [Description("Replacement plain-string tag set; null or empty removes all tags.")] IReadOnlyCollection<string>? tags = null,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync<TaskDetailDto>(
            commandService,
            "task.update",
            new TaskSaveRequest(
                id,
                title,
                taskTypeCode,
                body,
                bodyFormatCode,
                taskPriorityCode,
                taskSourceCode,
                sourceReference,
                sourceUrl,
                deadline,
                activeWaitingForLabel,
                tags),
            cancellationToken);

    [McpServerTool(Name = "task_get_timeline", ReadOnly = true, Destructive = false, Idempotent = true, OpenWorld = false)]
    [Description("Get the comments and application-generated log entries for one task, newest first.")]
    public static Task<IReadOnlyCollection<TaskTimelineItemDto>> GetTimelineAsync(
        ApplicationCommandService commandService,
        [Description("Numeric task ID.")] int taskId,
        CancellationToken cancellationToken = default) =>
        ExecuteAsync<IReadOnlyCollection<TaskTimelineItemDto>>(
            commandService,
            "task.timeline.get",
            new TaskTimelineRequest(taskId),
            cancellationToken);

    private static async Task<TResult> ExecuteAsync<TResult>(
        ApplicationCommandService commandService,
        string commandType,
        object payload,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await commandService.ExecuteAsync(
                new ApplicationCommand(commandType, JsonSerializer.SerializeToElement(payload, JsonOptions)),
                cancellationToken);

            return result is TResult typedResult
                ? typedResult
                : throw new McpException($"Application command '{commandType}' returned an unexpected result.");
        }
        catch (ValidationException exception)
        {
            throw new McpException(exception.Message, exception);
        }
        catch (BridgeException exception)
        {
            throw new McpException(exception.Message, exception);
        }
    }
}
