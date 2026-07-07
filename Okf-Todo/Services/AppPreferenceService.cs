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
        var preferences = new StoredPreferences(code);
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

        logger.LogInformation("Saved editor body format preference {BodyFormatCode}.", code);

        return new EditorPreferenceDto(code);
    }

    private async Task<StoredPreferences> ReadPreferencesAsync(CancellationToken cancellationToken)
    {
        var preferencesPath = pathProvider.GetPreferencesPath();
        if (!File.Exists(preferencesPath))
        {
            return new StoredPreferences(DefaultBodyFormatCode);
        }

        try
        {
            await using var stream = File.OpenRead(preferencesPath);
            return await JsonSerializer.DeserializeAsync<StoredPreferences>(
                stream,
                JsonOptions,
                cancellationToken) ?? new StoredPreferences(DefaultBodyFormatCode);
        }
        catch (JsonException exception)
        {
            logger.LogWarning(exception, "Could not read app preferences from {PreferencesPath}.", preferencesPath);
            return new StoredPreferences(DefaultBodyFormatCode);
        }
        catch (IOException exception)
        {
            logger.LogWarning(exception, "Could not read app preferences from {PreferencesPath}.", preferencesPath);
            return new StoredPreferences(DefaultBodyFormatCode);
        }
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

internal sealed record StoredPreferences(string? EditorBodyFormatCode);
