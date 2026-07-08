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
    private const string DefaultLayoutMode = LayoutPreferenceModes.Auto;
    private const double MinimumTaskListWidth = 160;
    private const double MaximumTaskListWidth = 2400;
    private const double MinimumTaskListHeight = 120;
    private const double MaximumTaskListHeight = 1800;
    private const int MinimumWindowWidth = 640;
    private const int MaximumWindowWidth = 10000;
    private const int MinimumWindowHeight = 480;
    private const int MaximumWindowHeight = 10000;
    private const int MinimumWindowCoordinate = -20000;
    private const int MaximumWindowCoordinate = 20000;
    private const bool DefaultWindowIsMaximized = true;
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
        var layoutMode = NormalizeOrDefaultLayoutMode(preferences.LayoutMode);

        return new LayoutPreferenceDto(preferences.TaskListWidth, preferences.TaskListHeight, layoutMode);
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
        var layoutMode = request.LayoutMode is null
            ? NormalizeOrDefaultLayoutMode(preferences.LayoutMode)
            : NormalizeLayoutMode(request.LayoutMode);

        preferences = preferences with
        {
            TaskListWidth = taskListWidth,
            TaskListHeight = taskListHeight,
            LayoutMode = layoutMode
        };
        await WritePreferencesAsync(preferences, cancellationToken);

        logger.LogInformation(
            "Saved layout preference with task list width {TaskListWidth}, height {TaskListHeight}, and mode {LayoutMode}.",
            taskListWidth,
            taskListHeight,
            layoutMode);

        return new LayoutPreferenceDto(taskListWidth, taskListHeight, layoutMode);
    }

    public async Task<WindowPreferenceDto> GetWindowPreferenceAsync(CancellationToken cancellationToken)
    {
        var preferences = await ReadPreferencesAsync(cancellationToken);
        return NormalizeOrDefaultWindowPreference(preferences);
    }

    public async Task<WindowPreferenceDto> SaveWindowPreferenceAsync(
        WindowPreferenceSaveRequest request,
        CancellationToken cancellationToken)
    {
        var preferences = await ReadPreferencesAsync(cancellationToken);
        var isMaximized = request.IsMaximized;
        var left = request.Left is null
            ? preferences.WindowLeft
            : NormalizeWindowCoordinate(request.Left, "left");
        var top = request.Top is null
            ? preferences.WindowTop
            : NormalizeWindowCoordinate(request.Top, "top");
        var width = request.Width is null
            ? preferences.WindowWidth
            : NormalizeWindowSize(
                request.Width,
                MinimumWindowWidth,
                MaximumWindowWidth,
                "width");
        var height = request.Height is null
            ? preferences.WindowHeight
            : NormalizeWindowSize(
                request.Height,
                MinimumWindowHeight,
                MaximumWindowHeight,
                "height");

        if (!isMaximized && (left is null || top is null || width is null || height is null))
        {
            throw new ValidationException("Window preference is incomplete.", "window");
        }

        preferences = preferences with
        {
            WindowLeft = left,
            WindowTop = top,
            WindowWidth = width,
            WindowHeight = height,
            WindowIsMaximized = isMaximized
        };
        await WritePreferencesAsync(preferences, cancellationToken);

        logger.LogInformation(
            "Saved window preference with left {WindowLeft}, top {WindowTop}, width {WindowWidth}, height {WindowHeight}, maximized {WindowIsMaximized}.",
            left,
            top,
            width,
            height,
            isMaximized);

        return new WindowPreferenceDto(left, top, width, height, isMaximized);
    }

    private async Task<StoredPreferences> ReadPreferencesAsync(CancellationToken cancellationToken)
    {
        var preferencesPath = pathProvider.GetPreferencesPath();
        if (!File.Exists(preferencesPath))
        {
            return CreateDefaultPreferences();
        }

        try
        {
            await using var stream = File.OpenRead(preferencesPath);
            return await JsonSerializer.DeserializeAsync<StoredPreferences>(
                stream,
                JsonOptions,
                cancellationToken) ?? CreateDefaultPreferences();
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "Could not read app preferences from {PreferencesPath}.", preferencesPath);
            return CreateDefaultPreferences();
        }
        catch (IOException exception)
        {
            logger.LogWarning(exception, "Could not read app preferences from {PreferencesPath}.", preferencesPath);
            return CreateDefaultPreferences();
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

    private static StoredPreferences CreateDefaultPreferences()
    {
        return new StoredPreferences(
            DefaultBodyFormatCode,
            null,
            null,
            DefaultLayoutMode,
            null,
            null,
            null,
            null,
            DefaultWindowIsMaximized);
    }

    private static string NormalizeOrDefaultLayoutMode(string? layoutMode)
    {
        try
        {
            return NormalizeLayoutMode(layoutMode);
        }
        catch (ValidationException)
        {
            return DefaultLayoutMode;
        }
    }

    private static string NormalizeLayoutMode(string? layoutMode)
    {
        var normalizedLayoutMode = string.IsNullOrWhiteSpace(layoutMode)
            ? DefaultLayoutMode
            : layoutMode.Trim().ToUpperInvariant();

        if (normalizedLayoutMode is LayoutPreferenceModes.Auto
            or LayoutPreferenceModes.SideBySide
            or LayoutPreferenceModes.Stacked)
        {
            return normalizedLayoutMode;
        }

        throw new ValidationException("Layout mode is invalid.", "layoutMode");
    }

    private WindowPreferenceDto NormalizeOrDefaultWindowPreference(StoredPreferences preferences)
    {
        try
        {
            var isMaximized = preferences.WindowIsMaximized ?? DefaultWindowIsMaximized;
            if (preferences.WindowLeft is null
                || preferences.WindowTop is null
                || preferences.WindowWidth is null
                || preferences.WindowHeight is null)
            {
                return new WindowPreferenceDto(null, null, null, null, isMaximized);
            }

            return new WindowPreferenceDto(
                NormalizeWindowCoordinate(preferences.WindowLeft, "left"),
                NormalizeWindowCoordinate(preferences.WindowTop, "top"),
                NormalizeWindowSize(preferences.WindowWidth, MinimumWindowWidth, MaximumWindowWidth, "width"),
                NormalizeWindowSize(preferences.WindowHeight, MinimumWindowHeight, MaximumWindowHeight, "height"),
                isMaximized);
        }
        catch (ValidationException exception)
        {
            logger.LogWarning(exception, "Stored window preference is invalid.");
            return new WindowPreferenceDto(null, null, null, null, DefaultWindowIsMaximized);
        }
    }

    private static int NormalizeWindowCoordinate(int? value, string field)
    {
        if (value is null || value < MinimumWindowCoordinate || value > MaximumWindowCoordinate)
        {
            throw new ValidationException("Window preference is invalid.", field);
        }

        return value.Value;
    }

    private static int NormalizeWindowSize(int? value, int minimum, int maximum, string field)
    {
        if (value is null || value < minimum || value > maximum)
        {
            throw new ValidationException("Window preference is invalid.", field);
        }

        return value.Value;
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

public static class LayoutPreferenceModes
{
    public const string Auto = "AUTO";
    public const string SideBySide = "SIDE_BY_SIDE";
    public const string Stacked = "STACKED";
}

public sealed record LayoutPreferenceDto(double? TaskListWidth, double? TaskListHeight, string LayoutMode);

public sealed record LayoutPreferenceSaveRequest(double? TaskListWidth, double? TaskListHeight, string? LayoutMode);

public sealed record WindowPreferenceDto(int? Left, int? Top, int? Width, int? Height, bool IsMaximized);

public sealed record WindowPreferenceSaveRequest(int? Left, int? Top, int? Width, int? Height, bool IsMaximized);

internal sealed record StoredPreferences(
    string? EditorBodyFormatCode,
    double? TaskListWidth,
    double? TaskListHeight,
    string? LayoutMode,
    int? WindowLeft,
    int? WindowTop,
    int? WindowWidth,
    int? WindowHeight,
    bool? WindowIsMaximized);
