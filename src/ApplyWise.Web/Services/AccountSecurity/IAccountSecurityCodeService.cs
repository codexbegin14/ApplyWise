using ApplyWise.Web.Models;

namespace ApplyWise.Web.Services.AccountSecurity;

public interface IAccountSecurityCodeService
{
    Task<SecurityCodeIssueResult> IssueAsync(string userId, string email, AccountSecurityAction action,
        CancellationToken cancellationToken = default);
    Task<SecurityCodeVerificationResult> VerifyAsync(string userId, AccountSecurityAction action, string? code,
        CancellationToken cancellationToken = default);
    Task ConsumeAsync(int codeId, CancellationToken cancellationToken = default);
}

public sealed record SecurityCodeIssueResult(bool Succeeded, string Message);
public sealed record SecurityCodeVerificationResult(bool Succeeded, int? CodeId, string Message);
