using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Photino.Okf_Todo.Data;

namespace Photino.Okf_Todo.Services;

public sealed class LookupSeedService(AppDbContext dbContext, ILogger<LookupSeedService> logger)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var seedPath = Path.Combine(AppContext.BaseDirectory, "lookup-seed.json");
        if (!File.Exists(seedPath))
        {
            throw new FileNotFoundException("Lookup seed configuration file was not found.", seedPath);
        }

        await using var stream = File.OpenRead(seedPath);
        var seed = await JsonSerializer.DeserializeAsync<LookupSeedConfiguration>(stream, JsonOptions, cancellationToken)
            ?? throw new InvalidOperationException("Lookup seed configuration is empty or invalid.");

        var now = DateTime.UtcNow;

        await SeedLookupAsync(dbContext.TaskTypes, seed.TaskTypes, now, cancellationToken);
        await SeedLookupAsync(dbContext.TaskStatuses, seed.TaskStatuses, now, cancellationToken);
        await SeedLookupAsync(dbContext.TaskPriorities, seed.TaskPriorities, now, cancellationToken);
        await SeedLookupAsync(dbContext.TaskSources, seed.TaskSources, now, cancellationToken);
        await SeedLookupAsync(dbContext.AttachmentKinds, seed.AttachmentKinds, now, cancellationToken);
        await SeedLookupAsync(dbContext.TaskLogTypes, seed.TaskLogTypes, now, cancellationToken);
        await SeedLookupAsync(dbContext.BodyFormats, seed.BodyFormats, now, cancellationToken);
        await SeedRelationTypesAsync(seed.TaskRelationTypes, now, cancellationToken);
        await SeedTagsAsync(seed.TaskTags, now, cancellationToken);

        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Lookup seeding completed from {SeedPath}.", seedPath);
    }

    private static async Task SeedLookupAsync<TLookup>(
        DbSet<TLookup> dbSet,
        IReadOnlyCollection<LookupSeedItem> seedItems,
        DateTime now,
        CancellationToken cancellationToken)
        where TLookup : LookupEntity, new()
    {
        if (await dbSet.AnyAsync(cancellationToken))
        {
            return;
        }

        dbSet.AddRange(seedItems.Select(item =>
        {
            var lookup = new TLookup
            {
                Code = item.Code,
                Name = item.Name,
                Description = item.Description,
                BackgroundColor = item.BackgroundColor,
                ForegroundColor = item.ForegroundColor,
                IsSelected = item.IsSelected,
                SortOrder = item.SortOrder,
                IsActive = item.IsActive,
                IsSystem = item.IsSystem,
                CreatedAt = now,
                UpdatedAt = now
            };

            return lookup;
        }));
    }

    private async Task SeedRelationTypesAsync(
        IReadOnlyCollection<TaskRelationTypeSeedItem> seedItems,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (await dbContext.TaskRelationTypes.AnyAsync(cancellationToken))
        {
            return;
        }

        dbContext.TaskRelationTypes.AddRange(seedItems.Select(item => new TaskRelationType
        {
            Code = item.Code,
            Name = item.Name,
            ReverseName = item.ReverseName,
            Description = item.Description,
            SortOrder = item.SortOrder,
            IsActive = item.IsActive,
            IsSystem = item.IsSystem,
            CreatedAt = now,
            UpdatedAt = now
        }));
    }

    private async Task SeedTagsAsync(
        IReadOnlyCollection<TaskTagSeedItem> seedItems,
        DateTime now,
        CancellationToken cancellationToken)
    {
        if (await dbContext.TaskTags.AnyAsync(cancellationToken))
        {
            return;
        }

        dbContext.TaskTags.AddRange(seedItems.Select(item => new TaskTag
        {
            Name = item.Name,
            Color = item.Color,
            SortOrder = item.SortOrder,
            IsActive = item.IsActive,
            CreatedAt = now,
            UpdatedAt = now
        }));
    }
}

public sealed record LookupSeedConfiguration(
    IReadOnlyCollection<LookupSeedItem> TaskTypes,
    IReadOnlyCollection<LookupSeedItem> TaskStatuses,
    IReadOnlyCollection<LookupSeedItem> TaskPriorities,
    IReadOnlyCollection<LookupSeedItem> TaskSources,
    IReadOnlyCollection<LookupSeedItem> AttachmentKinds,
    IReadOnlyCollection<TaskRelationTypeSeedItem> TaskRelationTypes,
    IReadOnlyCollection<LookupSeedItem> TaskLogTypes,
    IReadOnlyCollection<LookupSeedItem> BodyFormats,
    IReadOnlyCollection<TaskTagSeedItem> TaskTags);

public sealed record LookupSeedItem(
    string Code,
    string Name,
    int SortOrder,
    bool IsSystem = false,
    bool IsActive = true,
    string? Description = null,
    string? BackgroundColor = null,
    string? ForegroundColor = null,
    bool IsSelected = false);

public sealed record TaskRelationTypeSeedItem(
    string Code,
    string Name,
    string ReverseName,
    int SortOrder,
    bool IsSystem = false,
    bool IsActive = true,
    string? Description = null);

public sealed record TaskTagSeedItem(
    string Name,
    int SortOrder,
    bool IsActive = true,
    string? Color = null);
