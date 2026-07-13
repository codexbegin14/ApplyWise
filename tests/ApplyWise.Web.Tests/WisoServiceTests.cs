using ApplyWise.Web.Data;
using ApplyWise.Web.Models;
using ApplyWise.Web.Services.Wiso;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ApplyWise.Web.Tests;

public sealed class WisoServiceTests
{
    [Fact]
    public async Task Application_answer_only_counts_current_user()
    {
        await using var db = CreateDb();
        db.JobApplications.AddRange(
            new JobApplication { UserId = "one", CompanyName = "A", JobTitle = "Designer", Status = ApplicationStatus.Applied },
            new JobApplication { UserId = "two", CompanyName = "B", JobTitle = "Engineer", Status = ApplicationStatus.Offer });
        await db.SaveChangesAsync();
        var reply = await new WisoService(db).AskAsync("one", "How many applications do I have?");
        Assert.Contains("**1 applications**", reply.Message);
        Assert.DoesNotContain("Engineer", reply.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Empty_interview_answer_is_honest()
    {
        await using var db = CreateDb();
        var reply = await new WisoService(db).AskAsync("one", "When is my next interview?");
        Assert.Contains("don’t have any upcoming interviews", reply.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static ApplicationDbContext CreateDb() => new(new DbContextOptionsBuilder<ApplicationDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
}
