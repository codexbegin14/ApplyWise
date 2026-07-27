using System.Security.Cryptography;
using System.Text;
using ApplyWise.Web.Data;
using ApplyWise.Web.Models;
using ApplyWise.Web.Services.Email;
using Microsoft.EntityFrameworkCore;

namespace ApplyWise.Web.Services.AccountSecurity;

public sealed class AccountSecurityCodeService(
    ApplicationDbContext db,
    IApplicationEmailSender emailSender) : IAccountSecurityCodeService
{
    private const int MaximumAttempts = 5;
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(10);

    public async Task<SecurityCodeIssueResult> IssueAsync(string userId, string email, AccountSecurityAction action,
        CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var recent = await db.AccountSecurityCodes
            .Where(code => code.UserId == userId && code.Action == action && code.ConsumedAt == null)
            .OrderByDescending(code => code.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (recent is not null && recent.CreatedAt > now.AddMinutes(-1))
        {
            return new SecurityCodeIssueResult(false, "A code was sent recently. Please wait one minute before requesting another.");
        }

        var salt = RandomNumberGenerator.GetBytes(16);
        var value = RandomNumberGenerator.GetInt32(100_000, 1_000_000).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var record = new AccountSecurityCode
        {
            UserId = userId,
            Action = action,
            Salt = salt,
            CodeHash = Hash(salt, value),
            CreatedAt = now,
            ExpiresAt = now.Add(Lifetime)
        };

        db.AccountSecurityCodes.RemoveRange(db.AccountSecurityCodes.Where(code =>
            code.UserId == userId && code.Action == action && code.ConsumedAt == null));
        db.AccountSecurityCodes.Add(record);
        await db.SaveChangesAsync(cancellationToken);

        try
        {
            await emailSender.SendAccountSecurityCodeAsync(email, action, value);
        }
        catch
        {
            db.AccountSecurityCodes.Remove(record);
            await db.SaveChangesAsync(cancellationToken);
            throw;
        }

        return new SecurityCodeIssueResult(true, action switch
        {
            AccountSecurityAction.ConfirmEmail => "A six-digit verification code was sent to your email. It expires in 10 minutes.",
            AccountSecurityAction.ResetPassword => "A six-digit password reset code was sent to your email. It expires in 10 minutes.",
            _ => "A six-digit confirmation code was sent to your email. It expires in 10 minutes."
        });
    }

    public async Task<SecurityCodeVerificationResult> VerifyAsync(string userId, AccountSecurityAction action, string? code,
        CancellationToken cancellationToken = default)
    {
        var normalizedCode = (code ?? string.Empty).Trim();
        var record = await db.AccountSecurityCodes
            .Where(item => item.UserId == userId && item.Action == action && item.ConsumedAt == null)
            .OrderByDescending(item => item.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (record is null || record.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            return new SecurityCodeVerificationResult(false, null, "That code has expired. Request a new code and try again.");
        }

        var reserved = await ReserveAttemptAsync(record, cancellationToken);
        if (!reserved)
        {
            return new SecurityCodeVerificationResult(false, null, "Too many incorrect attempts. Request a new code and try again.");
        }

        if (normalizedCode.Length != 6
            || !CryptographicOperations.FixedTimeEquals(Hash(record.Salt, normalizedCode), record.CodeHash))
        {
            return new SecurityCodeVerificationResult(
                false,
                null,
                "That code is not correct. Request a new code if you reach the attempt limit.");
        }

        if (!await ConsumeVerifiedCodeAsync(record, cancellationToken))
        {
            return new SecurityCodeVerificationResult(
                false,
                null,
                "That code has already been used. Request a new code and try again.");
        }

        return new SecurityCodeVerificationResult(true, record.Id, string.Empty);
    }

    public async Task ConsumeAsync(int codeId, CancellationToken cancellationToken = default)
    {
        var record = await db.AccountSecurityCodes.SingleOrDefaultAsync(code => code.Id == codeId, cancellationToken);
        if (record is null) return;
        record.ConsumedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
    }

    private async Task<bool> ReserveAttemptAsync(
        AccountSecurityCode record,
        CancellationToken cancellationToken)
    {
        if (!db.Database.IsRelational())
        {
            if (record.FailedAttemptCount >= MaximumAttempts)
            {
                return false;
            }

            record.FailedAttemptCount++;
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }

        var now = DateTimeOffset.UtcNow;
        var updatedRows = await db.AccountSecurityCodes
            .Where(code =>
                code.Id == record.Id
                && code.ConsumedAt == null
                && code.ExpiresAt > now
                && code.FailedAttemptCount < MaximumAttempts)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    code => code.FailedAttemptCount,
                    code => code.FailedAttemptCount + 1),
                cancellationToken);
        return updatedRows == 1;
    }

    private async Task<bool> ConsumeVerifiedCodeAsync(
        AccountSecurityCode record,
        CancellationToken cancellationToken)
    {
        var consumedAt = DateTimeOffset.UtcNow;
        if (!db.Database.IsRelational())
        {
            if (record.ConsumedAt is not null)
            {
                return false;
            }

            record.ConsumedAt = consumedAt;
            await db.SaveChangesAsync(cancellationToken);
            return true;
        }

        var updatedRows = await db.AccountSecurityCodes
            .Where(code => code.Id == record.Id && code.ConsumedAt == null)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(code => code.ConsumedAt, consumedAt),
                cancellationToken);
        return updatedRows == 1;
    }

    private static byte[] Hash(byte[] salt, string value) => SHA256.HashData(Encoding.UTF8.GetBytes(Convert.ToHexString(salt) + value));
}
