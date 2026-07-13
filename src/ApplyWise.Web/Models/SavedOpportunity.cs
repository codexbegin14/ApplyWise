using Microsoft.AspNetCore.Identity;

namespace ApplyWise.Web.Models;

public class SavedOpportunity
{
    public int Id { get; set; }
    public required string UserId { get; set; }
    public int OpportunityId { get; set; }
    public DateTimeOffset SavedAt { get; set; }

    public IdentityUser? User { get; set; }
    public Opportunity? Opportunity { get; set; }
}
