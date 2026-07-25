using ApplyWise.Web.Data;
using ApplyWise.Web.Models;
using ApplyWise.Web.Services.AccountSecurity;
using ApplyWise.Web.Services.Email;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace ApplyWise.Web.Tests;

public class AccountSecurityCodeServiceTests
{
    [Fact]
    public async Task IssuedCode_IsSixDigitsHashedAndSingleUse()
    {
        await using var db = CreateContext();
        var emailSender = new CapturingEmailSender();
        var service = new AccountSecurityCodeService(db, emailSender);

        var issued = await service.IssueAsync(
            "user-1",
            "candidate@example.com",
            AccountSecurityAction.ConfirmEmail);

        Assert.True(issued.Succeeded);
        var delivery = Assert.Single(emailSender.Deliveries);
        Assert.Equal("candidate@example.com", delivery.Email);
        Assert.Equal(AccountSecurityAction.ConfirmEmail, delivery.Action);
        Assert.Matches(@"^\d{6}$", delivery.Code);

        var stored = Assert.Single(await db.AccountSecurityCodes.ToListAsync());
        Assert.Equal(32, stored.CodeHash.Length);
        Assert.Equal(16, stored.Salt.Length);
        Assert.DoesNotContain(delivery.Code, Convert.ToHexString(stored.CodeHash));

        var verification = await service.VerifyAsync(
            "user-1",
            AccountSecurityAction.ConfirmEmail,
            delivery.Code);

        Assert.True(verification.Succeeded);
        Assert.NotNull(verification.CodeId);
        await service.ConsumeAsync(verification.CodeId!.Value);

        var reused = await service.VerifyAsync(
            "user-1",
            AccountSecurityAction.ConfirmEmail,
            delivery.Code);
        Assert.False(reused.Succeeded);
    }

    [Fact]
    public async Task IssueAsync_EnforcesTheResendCooldown()
    {
        await using var db = CreateContext();
        var emailSender = new CapturingEmailSender();
        var service = new AccountSecurityCodeService(db, emailSender);

        var first = await service.IssueAsync(
            "user-2",
            "candidate@example.com",
            AccountSecurityAction.ResetPassword);
        var second = await service.IssueAsync(
            "user-2",
            "candidate@example.com",
            AccountSecurityAction.ResetPassword);

        Assert.True(first.Succeeded);
        Assert.False(second.Succeeded);
        Assert.Contains("wait one minute", second.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Single(emailSender.Deliveries);
    }

    [Fact]
    public async Task VerifyAsync_LocksTheCodeAfterFiveWrongAttempts()
    {
        await using var db = CreateContext();
        var emailSender = new CapturingEmailSender();
        var service = new AccountSecurityCodeService(db, emailSender);
        await service.IssueAsync(
            "user-3",
            "candidate@example.com",
            AccountSecurityAction.ResetPassword);

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var result = await service.VerifyAsync(
                "user-3",
                AccountSecurityAction.ResetPassword,
                "000000");
            Assert.False(result.Succeeded);
        }

        var delivery = Assert.Single(emailSender.Deliveries);
        var correctAfterLockout = await service.VerifyAsync(
            "user-3",
            AccountSecurityAction.ResetPassword,
            delivery.Code);

        Assert.False(correctAfterLockout.Succeeded);
        Assert.Contains("Too many", correctAfterLockout.Message);
    }

    private static ApplicationDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase("account-security-codes-" + Guid.NewGuid().ToString("N"))
            .Options);

    private sealed class CapturingEmailSender : IApplicationEmailSender
    {
        public List<Delivery> Deliveries { get; } = [];

        public Task SendAccountSecurityCodeAsync(
            string email,
            AccountSecurityAction action,
            string code)
        {
            Deliveries.Add(new Delivery(email, action, code));
            return Task.CompletedTask;
        }
    }

    private sealed record Delivery(string Email, AccountSecurityAction Action, string Code);
}
