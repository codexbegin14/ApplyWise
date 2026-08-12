using ApplyWise.Web.Data;
using ApplyWise.Web.Models;
using ApplyWise.Web.Services.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Xunit;

namespace ApplyWise.Web.Tests;

public sealed class WorkspaceQuotaTests
{
    [Fact]
    public async Task Application_quota_is_enforced_per_user()
    {
        var dbOptions = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("workspace-quota-" + Guid.NewGuid().ToString("N"))
            .Options;
        await using var db = new ApplicationDbContext(dbOptions);
        db.JobApplications.Add(new JobApplication
        {
            UserId = "owner",
            CompanyName = "Contoso",
            JobTitle = "Engineer",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var quotas = new WorkspaceQuotaService(db, Options.Create(new WorkspaceQuotaOptions
        {
            MaxApplicationsPerUser = 1
        }));

        Assert.False(await quotas.CanCreateApplicationAsync("owner"));
        Assert.True(await quotas.CanCreateApplicationAsync("different-user"));
    }
}
