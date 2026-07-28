using Microsoft.AspNetCore.DataProtection;

namespace ApplyWise.Web.Services.Gmail;

public interface IGmailCredentialProtector
{
    string Protect(string refreshToken);
    string Unprotect(string protectedRefreshToken);
}

public sealed class GmailCredentialProtector(IDataProtectionProvider dataProtectionProvider)
    : IGmailCredentialProtector
{
    private readonly IDataProtector _protector =
        dataProtectionProvider.CreateProtector("ApplyWise.GmailRefreshTokens.v1");

    public string Protect(string refreshToken) => _protector.Protect(refreshToken);

    public string Unprotect(string protectedRefreshToken) =>
        _protector.Unprotect(protectedRefreshToken);
}
