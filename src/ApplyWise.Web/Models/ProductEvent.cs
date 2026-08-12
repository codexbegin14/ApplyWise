using Microsoft.AspNetCore.Identity;

namespace ApplyWise.Web.Models;

public sealed class ProductEvent
{
    public long Id { get; set; }
    public string? UserId { get; set; }
    public required string Name { get; set; }
    public required string Source { get; set; }
    public bool Succeeded { get; set; }
    public DateTimeOffset OccurredAt { get; set; }

    public IdentityUser? User { get; set; }
}
