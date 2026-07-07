using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Photino.Okf_Todo.Data;

namespace Photino.Okf_Todo.Services;

public sealed class AppPreferenceService(
    AppDbContext dbContext,
    IAppPreferencePathProvider pathProvider,
    ILogger<AppPreferenceService> logger)
{
    private const string DefaultBodyFormatCode = "HTML";
    private const double MinimumTaskListWidth = 160;
    private const double MaximumTaskListWidth = 2400;
    private const double MinimumTaskListHeight = 120;
    private const double MaximumTaskListHeight = 1800;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async Task<EditorPreferenceDto> GetEditorPreferenceAsync(CancellationToken cancellationToken)
    {
        var preferences = await ReadPreferencesAsync(cancellationToken);
        var code = await NormalizeOrDefaultBodyFormatCodeAsync(
            preferences.EditorBodyFormatCode,
            cancellationToken);

        return new EditorPreferenceDto(code);
    }

    public async Task<EditorPreferenceDto> SaveEditorPreferenceAsync(
        EditorPreferenceSaveRequest request,
        CancellationToken cancellationToken)
    {
        var code = await NormalizeBodyFormatCodeAsync(request.BodyFormatCode, cancellationToken);
        var preferences = await ReadPreferencesAsync(cancellationToken);
        preferences = preferences with { EditorBodyFormatCode = code };
        await WritePreferencesAsync(preferences, cancellationToken);

        logger.LogInformation("Saved editor body format preference {BodyFormatCode}.", code);

        return new EditorPreferenceDto(code);
    }

    public async Task<LayoutPreferenceDto> GetLayoutPreferenceAsync(CancellationToken cancellationToken)
    {
        var preferences = await ReadPreferencesAsync(cancellationToken);

        return new LayoutPreferenceDto(preferences.TaskListWidth, preferences.TaskListHeight);
    }

    public async Task<LayoutPreferenceDto> SaveLayoutPreferenceAsync(
        LayoutPreferenceSaveRequest request,
        CancellationToken cancellationToken)
    {
        var preferences = await ReadPreferencesAsync(cancellationToken);
        var taskListWidth = request.TaskListWidth is null
            ? preferences.TaskListWidth
            : NormalizeLayoutValue(
                request.TaskListWidth,
                MinimumTaskListWidth,
                MaximumTaskListWidth,
                "taskListWidth");
        var taskListHeight = request.TaskListHeight is null
            ? preferences.TaskListHeight
            : NormalizeLayoutValue(
                request.TaskListHeight,
                MinimumTaskListHeight,
                MaximumTaskListHeight,
                "taskListHeight");

        preferences = preferences with
        {
            TaskListWidth = taskListWidth,
            TaskListHeight = taskListHeight
        };
        await WritePreferencesAsync(preferences, cancellationToken);

        logger.LogInformation(
            "Saved layout preference with task list width {TaskListWidth} and height {TaskListHeight}.",
            taskListWidth,
            taskListHeight);

        return new LayoutPreferenceDto(taskListWidth, taskListHeight);
    }

    private async Task<StoredPreferences> ReadPreferencesAsync(CancellationToken cancellationToken)
    {
        var preferencesPath = pathProvider.GetPreferencesPath();
        if (!File.Exists(preferencesPath))
        {
            return new StoredPreferences(DefaultBodyFormatCode, null, null);
        }

        try
        {
            await using var stream = File.OpenRead(preferencesPath);
            return await JsonSerializer.DeserializeAsync<StoredPreferences>(
                stream,
                JsonOptions,
                cancellationToken) ?? new StoredPreferences(DefaultBodyFormatCode, null, null);
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "Could not read app preferences from {PreferencesPath}.", preferencesPath);
            return new StoredPreferences(DefaultBodyFormatCode, null, null);
        }
        catch (IOException exception)
        {
            logger.LogWarning(exception, "Could not read app preferences from {PreferencesPath}.", preferencesPath);
            return new StoredPreferences(DefaultBodyFormatCode, null, null);
        }
    }

    private async Task WritePreferencesAsync(StoredPreferences preferences, CancellationToken cancellationToken)
    {
        var preferencesPath = pathProvider.GetPreferencesPath();
        var preferencesDirectory = Path.GetDirectoryName(preferencesPath);

        if (!string.IsNullOrWhiteSpace(preferencesDirectory))
        {
            Directory.CreateDirectory(preferencesDirectory);
        }

        await File.WriteAllTextAsync(
            preferencesPath,
            JsonSerializer.Serialize(preferences, JsonOptions),
            cancellationToken);
    }

    private async Task<string> NormalizeOrDefaultBodyFormatCodeAsync(
        string? code,
        CancellationToken cancellationToken)
    {
        try
        {
            return await NormalizeBodyFormatCodeAsync(code, cancellationToken);
        }
        catch (ValidationException exception)
        {
            logger.LogWarning(exception, "Stored editor body format preference is invalid.");
            return await NormalizeBodyFormatCodeAsync(DefaultBodyFormatCode, cancellationToken);
        }
    }

    private async Task<string> NormalizeBodyFormatCodeAsync(string? code, CancellationToken cancellationToken)
    {
        var normalizedCode = string.IsNullOrWhiteSpace(code)
            ? DefaultBodyFormatCode
            : code.Trim().ToUpperInvariant();

        var exists = await dbContext.BodyFormats
            .AsNoTracking()
            .AnyAsync(format => format.Code == normalizedCode, cancellationToken);

        if (!exists)
        {
            throw new ValidationException("Editor body format is invalid.", "bodyFormatCode");
        }

        return normalizedCode;
    }

    private static double NormalizeLayoutValue(
        double? value,
        double minimum,
        double maximum,
        string field)
    {
        if (value is null || !double.IsFinite(value.Value) || value.Value < minimum || value.Value > maximum)
        {
            throw new ValidationException("Layout preference is invalid.", field);
        }

        return Math.Round(value.Value);
    }
}

public interface IAppPreferencePathProvider
{
    string GetPreferencesPath();
}

public sealed class AppPreferencePathProvider : IAppPreferencePathProvider
{
    public string GetPreferencesPath()
    {
        var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var preferencesDirectory = Path.Combine(appDataPath, "Okf-Todo");

        return Path.Combine(preferencesDirectory, "app-preferences.json");
    }
}

public sealed record EditorPreferenceDto(string BodyFormatCode);

public sealed record EditorPreferenceSaveRequest(string? BodyFormatCode);

public sealed record LayoutPreferenceDto(double? TaskListWidth, double? TaskListHeight);

public sealed record LayoutPreferenceSaveRequest(double? TaskListWidth, double? TaskListHeight);

internal sealed record StoredPreferences(
    string? EditorBodyFormatCode,
    double? TaskListWidth,
    double? TaskListHeight);
