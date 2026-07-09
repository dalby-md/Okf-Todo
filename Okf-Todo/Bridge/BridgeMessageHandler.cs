using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Photino.Okf_Todo.Services;

namespace Photino.Okf_Todo.Bridge;

public sealed class BridgeMessageHandler(IServiceProvider services, ILogger<BridgeMessageHandler> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<string> HandleAsync(string message, CancellationToken cancellationToken = default)
    {
        BridgeRequest? request = null;

        try
        {
            request = JsonSerializer.Deserialize<BridgeRequest>(message, JsonOptions);
            if (request is null || string.IsNullOrWhiteSpace(request.MessageId) || string.IsNullOrWhiteSpace(request.Type))
            {
                return SerializeError(null, "InvalidMessage", "Bridge message is missing messageId or type.");
            }

            logger.LogInformation("Handling bridge request {MessageType} ({MessageId}).", request.Type, request.MessageId);

            await using var scope = services.CreateAsyncScope();
            var payload = await DispatchAsync(scope.ServiceProvider, request, cancellationToken);

            logger.LogInformation("Bridge request {MessageType} ({MessageId}) succeeded.", request.Type, request.MessageId);

            return JsonSerializer.Serialize(
                BridgeResponse.Success(request.MessageId, $"{request.Type}.result", payload),
                JsonOptions);
        }
        catch (ValidationException exception)
        {
            return SerializeError(request, "ValidationFailed", exception.Message, new { field = exception.Field });
        }
        catch (BridgeException exception)
        {
            return SerializeError(request, exception.Code, exception.Message);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Bridge request failed.");
            return SerializeError(request, "UnexpectedError", "Unexpected application error.");
        }
    }

    private static async Task<object> DispatchAsync(
        IServiceProvider scopedServices,
        BridgeRequest request,
        CancellationToken cancellationToken)
    {
        return request.Type switch
        {
            "issue.get" => await scopedServices.GetRequiredService<IssueService>()
                .GetOrCreateAsync(GetPayload<IssueGetRequest>(request).Id, cancellationToken),
            "issue.save" => await scopedServices.GetRequiredService<IssueService>()
                .SaveAsync(GetPayload<IssueSaveRequest>(request), cancellationToken),
            "image.create" => await scopedServices.GetRequiredService<ImageService>()
                .CreateAsync(GetPayload<ImageCreateRequest>(request), cancellationToken),
            "image.get" => await scopedServices.GetRequiredService<ImageService>()
                .GetAsync(GetPayload<ImageGetRequest>(request).Id, cancellationToken),
            "editor.preference.get" => await scopedServices.GetRequiredService<AppPreferenceService>()
                .GetEditorPreferenceAsync(cancellationToken),
            "editor.preference.save" => await scopedServices.GetRequiredService<AppPreferenceService>()
                .SaveEditorPreferenceAsync(GetPayload<EditorPreferenceSaveRequest>(request), cancellationToken),
            "layout.preference.get" => await scopedServices.GetRequiredService<AppPreferenceService>()
                .GetLayoutPreferenceAsync(cancellationToken),
            "layout.preference.save" => await scopedServices.GetRequiredService<AppPreferenceService>()
                .SaveLayoutPreferenceAsync(GetPayload<LayoutPreferenceSaveRequest>(request), cancellationToken),
            "task.lookups.get" => await scopedServices.GetRequiredService<TaskService>()
                .GetLookupsAsync(cancellationToken),
            "lookup.settings.get" => await scopedServices.GetRequiredService<TaskService>()
                .GetLookupSettingsAsync(cancellationToken),
            "lookup.settings.update" => await scopedServices.GetRequiredService<TaskService>()
                .UpdateLookupAsync(GetPayload<LookupUpdateRequest>(request), cancellationToken),
            "lookup.settings.create" => await scopedServices.GetRequiredService<TaskService>()
                .CreateLookupAsync(GetPayload<LookupCreateRequest>(request), cancellationToken),
            "task.list" => await scopedServices.GetRequiredService<TaskService>()
                .ListAsync(GetPayload<TaskListRequest>(request), cancellationToken),
            "task.get" => await scopedServices.GetRequiredService<TaskService>()
                .GetAsync(GetPayload<TaskGetRequest>(request).Id, cancellationToken),
            "task.create" => await scopedServices.GetRequiredService<TaskService>()
                .CreateAsync(GetPayload<TaskSaveRequest>(request), cancellationToken),
            "task.update" => await scopedServices.GetRequiredService<TaskService>()
                .UpdateAsync(GetPayload<TaskSaveRequest>(request), cancellationToken),
            "task.start" => await scopedServices.GetRequiredService<TaskService>()
                .StartAsync(GetPayload<TaskIdRequest>(request).Id, cancellationToken),
            "task.undoStart" => await scopedServices.GetRequiredService<TaskService>()
                .UndoStartAsync(GetPayload<TaskIdRequest>(request).Id, cancellationToken),
            "task.complete" => await scopedServices.GetRequiredService<TaskService>()
                .CompleteAsync(GetPayload<TaskIdRequest>(request).Id, cancellationToken),
            "task.reopen" => await scopedServices.GetRequiredService<TaskService>()
                .ReopenAsync(GetPayload<TaskIdRequest>(request).Id, cancellationToken),
            "task.cancel" => await scopedServices.GetRequiredService<TaskService>()
                .CancelAsync(GetPayload<TaskIdRequest>(request).Id, cancellationToken),
            "task.waiting.add" => await scopedServices.GetRequiredService<TaskService>()
                .AddWaitingForAsync(GetPayload<TaskWaitingForSaveRequest>(request), cancellationToken),
            "task.waiting.clear" => await scopedServices.GetRequiredService<TaskService>()
                .ClearWaitingForAsync(GetPayload<TaskIdRequest>(request).Id, cancellationToken),
            _ => throw new BridgeException("InvalidMessage", $"Unsupported bridge message type '{request.Type}'.")
        };
    }

    private static TPayload GetPayload<TPayload>(BridgeRequest request)
    {
        if (request.Payload is null)
        {
            throw new BridgeException("InvalidMessage", "Bridge message payload is required.");
        }

        var payload = request.Payload.Value.Deserialize<TPayload>(JsonOptions);
        return payload ?? throw new BridgeException("InvalidMessage", "Bridge message payload is invalid.");
    }

    private static string SerializeError(BridgeRequest? request, string code, string message, object? details = null)
    {
        return JsonSerializer.Serialize(
            BridgeResponse.Failure(
                request?.MessageId,
                request is null ? "app.error" : $"{request.Type}.result",
                code,
                message,
                details ?? new { }),
            JsonOptions);
    }
}

public sealed record BridgeRequest(string MessageId, string Type, JsonElement? Payload);

public sealed record ImageGetRequest(int Id);

public sealed record BridgeResponse(
    string? MessageId,
    string Type,
    bool Ok,
    object? Payload,
    BridgeError? Error)
{
    public static BridgeResponse Success(string messageId, string type, object payload)
    {
        return new BridgeResponse(messageId, type, true, payload, null);
    }

    public static BridgeResponse Failure(string? messageId, string type, string code, string message, object details)
    {
        return new BridgeResponse(messageId, type, false, null, new BridgeError(code, message, details));
    }
}

public sealed record BridgeError(string Code, string Message, object Details);
