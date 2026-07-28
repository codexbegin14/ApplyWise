using Microsoft.AspNetCore.Identity;

namespace ApplyWise.Web.Models;

public sealed class GmailConnection
{
    public int Id { get; set; }
    public required string UserId { get; set; }
    public string EmailAddress { get; set; } = string.Empty;
    public string ProtectedRefreshToken { get; set; } = string.Empty;
    public DateTimeOffset ConnectedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? LastSyncStartedAt { get; set; }
    public DateTimeOffset? LastSuccessfulSyncAt { get; set; }
    public DateTimeOffset NextSyncAt { get; set; }
    public string? LastErrorCode { get; set; }

    public IdentityUser? User { get; set; }
    public ICollection<ApplicationImport> Imports { get; set; } = [];
}
