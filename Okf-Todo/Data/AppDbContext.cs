using Microsoft.EntityFrameworkCore;

namespace Photino.Okf_Todo.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Issue> Issues => Set<Issue>();

    public DbSet<ImageAsset> Images => Set<ImageAsset>();

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
        });
    }
}
