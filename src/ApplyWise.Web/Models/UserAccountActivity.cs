using Microsoft.AspNetCore.Identity;

namespace ApplyWise.Web.Models;

public sealed class UserAccountActivity
{
    public required string UserId { get; set; }
    public DateTimeOffset RegisteredAt { get; set; }
    public DateTimeOffset? LastLoginAt { get; set; }
    public DateTimeOffset? LastActivityAt { get; set; }
    public string? LastLoginProvider { get; set; }
    public int TotalSuccessfulLogins { get; set; }

    public IdentityUser? User { get; set; }
}
