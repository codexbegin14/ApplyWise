using System.Net;
using ApplyWise.Web.Models;

namespace ApplyWise.Web.Services.Email;

public sealed record ApplyWiseEmailContent(string Subject, string HtmlBody, string TextBody);

public static class ApplyWiseEmailTemplate
{
    public static ApplyWiseEmailContent CreateSecurityCode(
        AccountSecurityAction action,
        string code,
        string? publicOrigin = null)
    {
        var details = action switch
        {
            AccountSecurityAction.ConfirmEmail => new EmailDetails(
                "Confirm your ApplyWise account",
                "Verify your email",
                "Use this code to confirm your email address and finish creating your ApplyWise account.",
                "Email verification code"),
            AccountSecurityAction.ResetPassword => new EmailDetails(
                "Reset your ApplyWise password",
                "Reset your password",
                "Use this code on ApplyWise to choose a new password for your account.",
                "Password reset code"),
            AccountSecurityAction.ChangePassword => new EmailDetails(
                "Confirm your ApplyWise password change",
                "Confirm your password change",
                "Use this code in ApplyWise settings to confirm that you want to change your password.",
                "Password change code"),
            AccountSecurityAction.DeleteAccount => new EmailDetails(
                "Confirm your ApplyWise account deletion",
                "Confirm account deletion",
                "Use this code in ApplyWise settings only if you intend to permanently delete your account.",
                "Account deletion code"),
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unsupported account security action.")
        };

        var normalizedCode = (code ?? string.Empty).Trim();
        var safeCode = WebUtility.HtmlEncode(normalizedCode);
        var securityNote = action == AccountSecurityAction.DeleteAccount
            ? "If you did not request account deletion, change your password and contact support."
            : "If you did not request this, you can safely ignore this email.";

        return new ApplyWiseEmailContent(
            details.Subject,
            BuildHtml(details.Heading, details.Introduction, details.CodeLabel, safeCode, securityNote, publicOrigin),
            $"""
            APPLYWISE

            {details.Heading}

            {details.Introduction}

            {details.CodeLabel}: {normalizedCode}

            This code expires in 10 minutes and can be used only once.
            Never share this code. ApplyWise will never ask for it by email, phone, or direct message.

            {securityNote}

            ApplyWise
            Track every application. Choose the right resume. Apply smarter.
            """);
    }

    public static ApplyWiseEmailContent CreateConfirmationLink(string confirmationLink, string? publicOrigin = null) =>
        CreateActionLink(
            "Confirm your ApplyWise email",
            "Confirm your email",
            "Confirm this email address to finish securing your ApplyWise account.",
            "Confirm email",
            confirmationLink,
            publicOrigin);

    public static ApplyWiseEmailContent CreatePasswordResetLink(string resetLink, string? publicOrigin = null) =>
        CreateActionLink(
            "Reset your ApplyWise password",
            "Reset your password",
            "A password reset was requested for your ApplyWise account.",
            "Reset password",
            resetLink,
            publicOrigin);

    private static ApplyWiseEmailContent CreateActionLink(
        string subject,
        string heading,
        string introduction,
        string buttonLabel,
        string link,
        string? publicOrigin)
    {
        var safeLink = WebUtility.HtmlEncode(link);
        var safeButtonLabel = WebUtility.HtmlEncode(buttonLabel);
        var wiso = BuildWiso(publicOrigin);
        var html = $"""
            <!doctype html>
            <html lang="en">
            <body style="margin:0;padding:0;background:#f4f7fb;color:#0f172a;font-family:Arial,'Segoe UI',sans-serif;">
              <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="background:#f4f7fb;padding:32px 12px;">
                <tr><td align="center">
                  <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="max-width:600px;background:#ffffff;border:1px solid #dbe4f0;border-radius:16px;overflow:hidden;">
                    <tr><td style="padding:22px 30px;background:#eff6ff;border-bottom:1px solid #dbe4f0;">
                      <strong style="font-size:20px;color:#1d4ed8;">ApplyWise</strong>
                    </td></tr>
                    <tr><td style="padding:34px 30px;">
                      {wiso}
                      <p style="margin:0 0 8px;color:#2563eb;font-size:12px;font-weight:700;letter-spacing:.08em;text-transform:uppercase;">Account security</p>
                      <h1 style="margin:0 0 14px;color:#0f172a;font-size:28px;line-height:1.2;">{WebUtility.HtmlEncode(heading)}</h1>
                      <p style="margin:0 0 24px;color:#475569;font-size:16px;line-height:1.65;">{WebUtility.HtmlEncode(introduction)}</p>
                      <p style="margin:0 0 26px;">
                        <a href="{safeLink}" style="display:inline-block;padding:13px 20px;border-radius:8px;background:#2563eb;color:#ffffff;font-size:15px;font-weight:700;text-decoration:none;">{safeButtonLabel}</a>
                      </p>
                      <p style="margin:0 0 8px;color:#64748b;font-size:13px;line-height:1.55;">If the button does not work, copy and paste this address into your browser:</p>
                      <p style="margin:0;word-break:break-all;color:#1d4ed8;font-size:13px;line-height:1.55;">{safeLink}</p>
                      <div style="margin-top:26px;padding:16px;border-radius:10px;background:#f8fafc;color:#475569;font-size:13px;line-height:1.55;">
                        If you did not request this, you can safely ignore this email. Never forward account-security emails.
                      </div>
                    </td></tr>
                    {BuildFooter()}
                  </table>
                </td></tr>
              </table>
            </body>
            </html>
            """;

        return new ApplyWiseEmailContent(
            subject,
            html,
            $"""
            APPLYWISE

            {heading}

            {introduction}

            {buttonLabel}: {link}

            If you did not request this, you can safely ignore this email.

            ApplyWise
            Track every application. Choose the right resume. Apply smarter.
            """);
    }

    private static string BuildHtml(
        string heading,
        string introduction,
        string codeLabel,
        string safeCode,
        string securityNote,
        string? publicOrigin)
    {
        var wiso = BuildWiso(publicOrigin);
        return $"""
            <!doctype html>
            <html lang="en">
            <body style="margin:0;padding:0;background:#f4f7fb;color:#0f172a;font-family:Arial,'Segoe UI',sans-serif;">
              <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="background:#f4f7fb;padding:32px 12px;">
                <tr><td align="center">
                  <table role="presentation" width="100%" cellspacing="0" cellpadding="0" style="max-width:600px;background:#ffffff;border:1px solid #dbe4f0;border-radius:16px;overflow:hidden;">
                    <tr><td style="padding:22px 30px;background:#eff6ff;border-bottom:1px solid #dbe4f0;">
                      <strong style="font-size:20px;color:#1d4ed8;">ApplyWise</strong>
                    </td></tr>
                    <tr><td style="padding:34px 30px;">
                      {wiso}
                      <p style="margin:0 0 8px;color:#2563eb;font-size:12px;font-weight:700;letter-spacing:.08em;text-transform:uppercase;">Account security</p>
                      <h1 style="margin:0 0 14px;color:#0f172a;font-size:28px;line-height:1.2;">{WebUtility.HtmlEncode(heading)}</h1>
                      <p style="margin:0 0 22px;color:#475569;font-size:16px;line-height:1.65;">{WebUtility.HtmlEncode(introduction)}</p>
                      <p style="margin:0 0 8px;color:#64748b;font-size:12px;font-weight:700;letter-spacing:.06em;text-transform:uppercase;">{WebUtility.HtmlEncode(codeLabel)}</p>
                      <div style="margin:0 0 22px;padding:18px 16px;border:1px solid #bfdbfe;border-radius:10px;background:#eff6ff;color:#0f172a;font-family:Consolas,'Courier New',monospace;font-size:32px;font-weight:700;letter-spacing:9px;text-align:center;">{safeCode}</div>
                      <p style="margin:0 0 8px;color:#475569;font-size:14px;line-height:1.6;"><strong>This code expires in 10 minutes</strong> and can be used only once.</p>
                      <p style="margin:0;color:#475569;font-size:14px;line-height:1.6;">Never share this code. ApplyWise will never ask for it by email, phone, or direct message.</p>
                      <div style="margin-top:24px;padding:16px;border-radius:10px;background:#f8fafc;color:#475569;font-size:13px;line-height:1.55;">
                        {WebUtility.HtmlEncode(securityNote)}
                      </div>
                    </td></tr>
                    {BuildFooter()}
                  </table>
                </td></tr>
              </table>
            </body>
            </html>
            """;
    }

    private static string BuildWiso(string? publicOrigin)
    {
        if (!Uri.TryCreate(publicOrigin, UriKind.Absolute, out var origin) || origin.Scheme != Uri.UriSchemeHttps)
        {
            return string.Empty;
        }

        var wisoUrl = WebUtility.HtmlEncode(new Uri(origin, "/images/wiso.png").ToString());
        return $"""
            <div style="float:right;margin:-12px 0 14px 18px;width:92px;text-align:center;">
              <img src="{wisoUrl}" width="92" height="92" alt="Wiso, the ApplyWise guide" style="display:block;width:92px;height:92px;object-fit:contain;border:0;" />
            </div>
            """;
    }

    private static string BuildFooter() =>
        """
        <tr><td style="padding:20px 30px;background:#0f172a;color:#dbeafe;font-size:12px;line-height:1.6;text-align:center;">
          <strong style="color:#ffffff;">ApplyWise</strong><br />
          Track every application. Choose the right resume. Apply smarter.
        </td></tr>
        """;

    private sealed record EmailDetails(string Subject, string Heading, string Introduction, string CodeLabel);
}
