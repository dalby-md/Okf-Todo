using Microsoft.EntityFrameworkCore;

namespace Photino.Okf_Todo.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Issue> Issues => Set<Issue>();

    public DbSet<ImageAsset> Images => Set<ImageAsset>();

    public DbSet<TaskItem> TaskItems => Set<TaskItem>();

    public DbSet<TaskType> TaskTypes => Set<TaskType>();

    public DbSet<TaskStatus> TaskStatuses => Set<TaskStatus>();

    public DbSet<TaskPriority> TaskPriorities => Set<TaskPriority>();

    public DbSet<TaskSource> TaskSources => Set<TaskSource>();

    public DbSet<TaskWaitingFor> TaskWaitingFors => Set<TaskWaitingFor>();

    public DbSet<TaskComment> TaskComments => Set<TaskComment>();

    public DbSet<TaskLogEntry> TaskLogEntries => Set<TaskLogEntry>();

    public DbSet<TaskLogType> TaskLogTypes => Set<TaskLogType>();

    public DbSet<TaskChecklistItem> TaskChecklistItems => Set<TaskChecklistItem>();

    public DbSet<TaskAttachment> TaskAttachments => Set<TaskAttachment>();

    public DbSet<AttachmentKind> AttachmentKinds => Set<AttachmentKind>();

    public DbSet<TaskTag> TaskTags => Set<TaskTag>();

    public DbSet<TaskTaskTag> TaskTaskTags => Set<TaskTaskTag>();

    public DbSet<TaskRelation> TaskRelations => Set<TaskRelation>();

    public DbSet<TaskRelationType> TaskRelationTypes => Set<TaskRelationType>();

    public DbSet<BodyFormat> BodyFormats => Set<BodyFormat>();

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await NormalizeSelectedLookupsAsync(cancellationToken);
        return await base.SaveChangesAsync(cancellationToken);
    }

    public override async Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        await NormalizeSelectedLookupsAsync(cancellationToken);
        return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Issue>(entity =>
        {
            entity.Property(issue => issue.Title).IsRequired();
            entity.Property(issue => issue.Status).IsRequired();
            entity.Property(issue => issue.BodyHtml).IsRequired();
            entity.Property(issue => issue.BodyMarkdown).IsRequired();
            entity.Property(issue => issue.EditorMode).IsRequired();
            entity.Property(issue => issue.CreatedUtc).IsRequired();
            entity.Property(issue => issue.ModifiedUtc).IsRequired();

            entity.HasMany(issue => issue.Images)
                .WithOne(image => image.Issue)
                .HasForeignKey(image => image.IssueId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ImageAsset>(entity =>
        {
            entity.Property(image => image.MimeType).IsRequired();
            entity.Property(image => image.ImageData).IsRequired();
            entity.Property(image => image.CreatedUtc).IsRequired();

            entity.ToTable(table => table.HasCheckConstraint(
                "CK_Images_OneOwner",
                $"({nameof(ImageAsset.IssueId)} IS NOT NULL AND {nameof(ImageAsset.TaskId)} IS NULL) OR ({nameof(ImageAsset.IssueId)} IS NULL AND {nameof(ImageAsset.TaskId)} IS NOT NULL)"));

            entity.HasOne(image => image.Issue)
                .WithMany(issue => issue.Images)
                .HasForeignKey(image => image.IssueId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(image => image.Task)
                .WithMany(task => task.Images)
                .HasForeignKey(image => image.TaskId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        ConfigureLookup<TaskType>(modelBuilder);
        ConfigureLookup<TaskStatus>(modelBuilder);
        ConfigureLookup<TaskPriority>(modelBuilder);
        ConfigureLookup<TaskSource>(modelBuilder);
        ConfigureLookup<AttachmentKind>(modelBuilder);
        ConfigureLookup<TaskLogType>(modelBuilder);
        ConfigureLookup<BodyFormat>(modelBuilder);

        modelBuilder.Entity<TaskRelationType>(entity =>
        {
            ConfigureLookupEntity(entity);
            entity.Property(relationType => relationType.ReverseName).IsRequired();
        });

        modelBuilder.Entity<TaskItem>(entity =>
        {
            entity.Property(task => task.Title).IsRequired();
            entity.Property(task => task.CreatedAt).IsRequired();
            entity.Property(task => task.UpdatedAt).IsRequired();

            entity.HasOne(task => task.BodyFormat)
                .WithMany(bodyFormat => bodyFormat.Tasks)
                .HasForeignKey(task => task.BodyFormatId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(task => task.TaskType)
                .WithMany(taskType => taskType.Tasks)
                .HasForeignKey(task => task.TaskTypeId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(task => task.TaskStatus)
                .WithMany(taskStatus => taskStatus.Tasks)
                .HasForeignKey(task => task.TaskStatusId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(task => task.TaskPriority)
                .WithMany(priority => priority.Tasks)
                .HasForeignKey(task => task.TaskPriorityId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(task => task.TaskSource)
                .WithMany(source => source.Tasks)
                .HasForeignKey(task => task.TaskSourceId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasMany(task => task.Images)
                .WithOne(image => image.Task)
                .HasForeignKey(image => image.TaskId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TaskWaitingFor>(entity =>
        {
            entity.Property(waitingFor => waitingFor.Label).IsRequired();
            entity.Property(waitingFor => waitingFor.WaitingSince).IsRequired();
            entity.Property(waitingFor => waitingFor.CreatedAt).IsRequired();
            entity.Property(waitingFor => waitingFor.UpdatedAt).IsRequired();

            entity.HasOne(waitingFor => waitingFor.Task)
                .WithMany(task => task.WaitingTargets)
                .HasForeignKey(waitingFor => waitingFor.TaskId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(waitingFor => waitingFor.TaskId)
                .IsUnique()
                .HasFilter($"{nameof(TaskWaitingFor.ResolvedAt)} IS NULL");
        });

        modelBuilder.Entity<TaskComment>(entity =>
        {
            entity.Property(comment => comment.CommentText).IsRequired();
            entity.Property(comment => comment.CreatedAt).IsRequired();

            entity.HasOne(comment => comment.Task)
                .WithMany(task => task.Comments)
                .HasForeignKey(comment => comment.TaskId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TaskLogEntry>(entity =>
        {
            entity.Property(log => log.Message).IsRequired();
            entity.Property(log => log.CreatedAt).IsRequired();

            entity.HasOne(log => log.Task)
                .WithMany(task => task.LogEntries)
                .HasForeignKey(log => log.TaskId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(log => log.TaskLogType)
                .WithMany(logType => logType.LogEntries)
                .HasForeignKey(log => log.TaskLogTypeId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TaskChecklistItem>(entity =>
        {
            entity.Property(item => item.Text).IsRequired();
            entity.Property(item => item.CreatedAt).IsRequired();
            entity.Property(item => item.UpdatedAt).IsRequired();

            entity.HasOne(item => item.Task)
                .WithMany(task => task.ChecklistItems)
                .HasForeignKey(item => item.TaskId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TaskAttachment>(entity =>
        {
            entity.Property(attachment => attachment.FileName).IsRequired();
            entity.Property(attachment => attachment.ContentBlob).IsRequired();
            entity.Property(attachment => attachment.CreatedAt).IsRequired();

            entity.HasOne(attachment => attachment.Task)
                .WithMany(task => task.Attachments)
                .HasForeignKey(attachment => attachment.TaskId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(attachment => attachment.AttachmentKind)
                .WithMany(kind => kind.Attachments)
                .HasForeignKey(attachment => attachment.AttachmentKindId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<TaskTag>(entity =>
        {
            entity.Property(tag => tag.Name).IsRequired();
            entity.Property(tag => tag.CreatedAt).IsRequired();
            entity.Property(tag => tag.UpdatedAt).IsRequired();
            entity.HasIndex(tag => tag.Name).IsUnique();
        });

        modelBuilder.Entity<TaskTaskTag>(entity =>
        {
            entity.HasKey(taskTag => new { taskTag.TaskId, taskTag.TaskTagId });
            entity.Property(taskTag => taskTag.CreatedAt).IsRequired();

            entity.HasOne(taskTag => taskTag.Task)
                .WithMany(task => task.TaskTags)
                .HasForeignKey(taskTag => taskTag.TaskId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(taskTag => taskTag.TaskTag)
                .WithMany(tag => tag.TaskTags)
                .HasForeignKey(taskTag => taskTag.TaskTagId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TaskRelation>(entity =>
        {
            entity.Property(relation => relation.CreatedAt).IsRequired();

            entity.ToTable(table => table.HasCheckConstraint(
                "CK_TaskRelations_SourceTarget_Different",
                $"{nameof(TaskRelation.SourceTaskId)} <> {nameof(TaskRelation.TargetTaskId)}"));

            entity.HasOne(relation => relation.SourceTask)
                .WithMany(task => task.SourceRelations)
                .HasForeignKey(relation => relation.SourceTaskId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(relation => relation.TargetTask)
                .WithMany(task => task.TargetRelations)
                .HasForeignKey(relation => relation.TargetTaskId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(relation => relation.TaskRelationType)
                .WithMany(type => type.Relations)
                .HasForeignKey(relation => relation.TaskRelationTypeId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }

    private static void ConfigureLookup<TLookup>(ModelBuilder modelBuilder)
        where TLookup : LookupEntity
    {
        modelBuilder.Entity<TLookup>(ConfigureLookupEntity);
    }

    private static void ConfigureLookupEntity<TLookup>(Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<TLookup> entity)
        where TLookup : LookupEntity
    {
        entity.Property(lookup => lookup.Code).IsRequired();
        entity.Property(lookup => lookup.Name).IsRequired();
        entity.Property(lookup => lookup.CreatedAt).IsRequired();
        entity.Property(lookup => lookup.UpdatedAt).IsRequired();
        entity.Property(lookup => lookup.BackgroundColor).HasMaxLength(32);
        entity.Property(lookup => lookup.ForegroundColor).HasMaxLength(32);
        entity.Property(lookup => lookup.IsSelected).HasDefaultValue(false);
        entity.HasIndex(lookup => lookup.Code).IsUnique();
    }

    private async Task NormalizeSelectedLookupsAsync(CancellationToken cancellationToken)
    {
        await NormalizeSelectedLookupAsync<AttachmentKind>(cancellationToken);
        await NormalizeSelectedLookupAsync<BodyFormat>(cancellationToken);
        await NormalizeSelectedLookupAsync<TaskLogType>(cancellationToken);
        await NormalizeSelectedLookupAsync<TaskType>(cancellationToken);
        await NormalizeSelectedLookupAsync<TaskStatus>(cancellationToken);
        await NormalizeSelectedLookupAsync<TaskPriority>(cancellationToken);
        await NormalizeSelectedLookupAsync<TaskRelationType>(cancellationToken);
        await NormalizeSelectedLookupAsync<TaskSource>(cancellationToken);
    }

    private async Task NormalizeSelectedLookupAsync<TLookup>(CancellationToken cancellationToken)
        where TLookup : LookupEntity
    {
        var selectedEntries = ChangeTracker.Entries<TLookup>()
            .Where(entry => entry.Entity.IsSelected
                && entry.State is EntityState.Added or EntityState.Modified)
            .ToList();

        if (selectedEntries.Count == 0)
        {
            return;
        }

        var selectedEntity = selectedEntries[^1].Entity;

        foreach (var entry in ChangeTracker.Entries<TLookup>()
            .Where(entry => !ReferenceEquals(entry.Entity, selectedEntity) && entry.Entity.IsSelected))
        {
            entry.Entity.IsSelected = false;
            if (entry.State == EntityState.Unchanged)
            {
                entry.State = EntityState.Modified;
            }
        }

        var query = selectedEntity.Id == 0
            ? Set<TLookup>().Where(lookup => lookup.IsSelected)
            : Set<TLookup>().Where(lookup => lookup.Id != selectedEntity.Id && lookup.IsSelected);

        await query.ExecuteUpdateAsync(
            updates => updates.SetProperty(lookup => lookup.IsSelected, false),
            cancellationToken);
    }
}
