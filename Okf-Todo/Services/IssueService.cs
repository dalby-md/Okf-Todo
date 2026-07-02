using Microsoft.EntityFrameworkCore;
using Photino.Okf_Todo.Data;

namespace Photino.Okf_Todo.Services;

public sealed class IssueService(AppDbContext dbContext, HtmlSanitizerService htmlSanitizer)
{
    public async Task<IssueDto> GetOrCreateAsync(int id, CancellationToken cancellationToken)
    {
        var issue = await dbContext.Issues.FindAsync([id], cancellationToken);

        if (issue is not null)
        {
            return IssueDto.FromIssue(issue);
        }

        var now = DateTime.UtcNow;
        issue = new Issue
        {
            Id = id,
            Title = "Untitled text",
            Status = "Open",
            Priority = 0,
            BodyHtml = "<p>Start writing your text here.</p>",
            CreatedUtc = now,
            ModifiedUtc = now
        };

        dbContext.Issues.Add(issue);
        await dbContext.SaveChangesAsync(cancellationToken);

        return IssueDto.FromIssue(issue);
    }

    public async Task<IssueDto> SaveAsync(IssueSaveRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Title))
        {
            throw new ValidationException("Title is required.", "title");
        }

        var now = DateTime.UtcNow;
        var issue = await dbContext.Issues.FindAsync([request.Id], cancellationToken);

        if (issue is null)
        {
            issue = new Issue
            {
                Id = request.Id,
                CreatedUtc = now
            };

            dbContext.Issues.Add(issue);
        }

        issue.Title = request.Title.Trim();
        issue.Status = string.IsNullOrWhiteSpace(request.Status) ? "Open" : request.Status.Trim();
        issue.Priority = request.Priority;
        issue.DueUtc = request.DueUtc;
        issue.BodyHtml = htmlSanitizer.Sanitize(request.BodyHtml ?? string.Empty);
        issue.ModifiedUtc = now;

        await dbContext.SaveChangesAsync(cancellationToken);

        return IssueDto.FromIssue(issue);
    }
}

public sealed record IssueDto(
    int Id,
    string Title,
    string Status,
    int Priority,
    DateTime? DueUtc,
    DateTime CreatedUtc,
    DateTime ModifiedUtc,
    string BodyHtml)
{
    public static IssueDto FromIssue(Issue issue)
    {
        return new IssueDto(
            issue.Id,
            issue.Title,
            issue.Status,
            issue.Priority,
            issue.DueUtc,
            issue.CreatedUtc,
            issue.ModifiedUtc,
            issue.BodyHtml);
    }
}

public sealed record IssueSaveRequest(
    int Id,
    string Title,
    string? Status,
    int Priority,
    DateTime? DueUtc,
    string? BodyHtml);

public sealed record IssueGetRequest(int Id);

public sealed class ValidationException(string message, string field) : Exception(message)
{
    public string Field { get; } = field;
}
