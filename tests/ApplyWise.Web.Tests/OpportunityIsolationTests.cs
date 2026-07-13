using ApplyWise.Web.Data;
using ApplyWise.Web.Models;
using ApplyWise.Web.Services.Opportunities;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ApplyWise.Web.Tests;

public sealed class OpportunityIsolationTests
{
    [Fact]
    public async Task Expired_opportunities_are_not_returned()
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        db.Opportunities.AddRange(
            new Opportunity { Title = "Current", OrganizationName = "Public", Category = OpportunityCategory.Government, EmploymentType = OpportunityEmploymentType.FullTime, WorkMode = OpportunityWorkMode.Onsite, Summary = "Current", ApplicationUrl = "https://example.com/current", NormalizedKey = "CURRENT", Status = OpportunityStatus.Published, PublishedAt = DateTimeOffset.UtcNow },
            new Opportunity { Title = "Expired", OrganizationName = "Private", Category = OpportunityCategory.Private, EmploymentType = OpportunityEmploymentType.FullTime, WorkMode = OpportunityWorkMode.Remote, Summary = "Expired", ApplicationUrl = "https://example.com/expired", NormalizedKey = "EXPIRED", Status = OpportunityStatus.Published, PublishedAt = DateTimeOffset.UtcNow.AddDays(-2), Deadline = DateTimeOffset.UtcNow.AddDays(-1) });
        await db.SaveChangesAsync();
        var result = await new OpportunityService(db).GetFeedAsync("user", new());
        Assert.Single(result.Items);
        Assert.Equal("Current", result.Items[0].Title);
    }

    [Fact]
    public async Task Saved_state_is_scoped_to_the_requesting_user()
    {
        await using var db = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var opportunity = new Opportunity { Title = "Remote role", OrganizationName = "Public", Category = OpportunityCategory.Private, EmploymentType = OpportunityEmploymentType.FullTime, WorkMode = OpportunityWorkMode.Remote, Summary = "Role", ApplicationUrl = "https://example.com/role", NormalizedKey = "ROLE", Status = OpportunityStatus.Published, PublishedAt = DateTimeOffset.UtcNow };
        db.Opportunities.Add(opportunity); await db.SaveChangesAsync();
        db.SavedOpportunities.Add(new SavedOpportunity { UserId = "other-user", OpportunityId = opportunity.Id, SavedAt = DateTimeOffset.UtcNow }); await db.SaveChangesAsync();
        var result = await new OpportunityService(db).GetFeedAsync("current-user", new());
        Assert.False(result.Items.Single().IsSaved);
    }
}
