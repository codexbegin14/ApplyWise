using Microsoft.AspNetCore.Identity;

namespace ApplyWise.Web.Models;

public sealed class ApplicationImport
{
    public int Id { get; set; }
    public required string UserId { get; set; }
    public int GmailConnectionId { get; set; }
    public string ExternalMessageId { get; set; } = string.Empty;
    public string? ExternalThreadId { get; set; }
    public ApplicationImportDirection Direction { get; set; }
    public ApplicationImportStatus Status { get; set; }
    public int Confidence { get; set; }
    public string EmailSubject { get; set; } = string.Empty;
    public string? SenderDomain { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string JobTitle { get; set; } = string.Empty;
    public string? JobLocation { get; set; }
    public JobSource Source { get; set; }
    public string? JobUrl { get; set; }
    public DateOnly? AppliedDate { get; set; }
    public string? ResumeFileName { get; set; }
    public int? CreatedApplicationId { get; set; }
    public DateTimeOffset DetectedAt { get; set; }
    public DateTimeOffset? ReviewedAt { get; set; }

    public IdentityUser? User { get; set; }
    public GmailConnection? GmailConnection { get; set; }
}

public enum ApplicationImportDirection
{
    Incoming = 1,
    Outgoing = 2
}

public enum ApplicationImportStatus
{
    PendingReview = 1,
    Accepted = 2,
    Dismissed = 3
}
