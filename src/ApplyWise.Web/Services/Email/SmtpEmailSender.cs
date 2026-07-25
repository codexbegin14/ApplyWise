using System.Net;
using System.Net.Mail;
using System.Net.Mime;
using System.Text;
using ApplyWise.Web.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace ApplyWise.Web.Services.Email;

public sealed class SmtpEmailSender(IOptions<EmailOptions> options, IWebHostEnvironment environment,
    IConfiguration configuration,
    ILogger<SmtpEmailSender> logger) : IEmailSender<IdentityUser>, IApplicationEmailSender
{
    private readonly EmailOptions settings = options.Value;
    private readonly string? publicOrigin = configuration["PublicOrigin"]?.TrimEnd('/');

    public Task SendConfirmationLinkAsync(IdentityUser user, string email, string confirmationLink) =>
        SendAsync(email, ApplyWiseEmailTemplate.CreateConfirmationLink(confirmationLink, publicOrigin));

    public Task SendPasswordResetLinkAsync(IdentityUser user, string email, string resetLink) =>
        SendAsync(email, ApplyWiseEmailTemplate.CreatePasswordResetLink(resetLink, publicOrigin));

    public Task SendPasswordResetCodeAsync(IdentityUser user, string email, string resetCode) =>
        SendAsync(email, ApplyWiseEmailTemplate.CreateSecurityCode(
            AccountSecurityAction.ResetPassword,
            resetCode,
            publicOrigin));

    public Task SendAccountSecurityCodeAsync(string email, AccountSecurityAction action, string code) =>
        SendAsync(email, ApplyWiseEmailTemplate.CreateSecurityCode(action, code, publicOrigin));

    private async Task SendAsync(string recipient, ApplyWiseEmailContent content)
    {
        if (string.IsNullOrWhiteSpace(settings.Host) || string.IsNullOrWhiteSpace(settings.From))
        {
            if (environment.IsDevelopment())
            {
                logger.LogInformation(
                    "Development email prepared for {Recipient}: {Subject}. Configure SMTP to deliver it.",
                    recipient,
                    content.Subject);
                return;
            }

            throw new InvalidOperationException("Email:Host and Email:From must be configured outside Development.");
        }

        using var client = new SmtpClient(settings.Host, settings.Port)
        {
            EnableSsl = settings.UseSsl,
            Credentials = string.IsNullOrWhiteSpace(settings.UserName)
                ? CredentialCache.DefaultNetworkCredentials
                : new NetworkCredential(settings.UserName, settings.Password)
        };

        using var message = new MailMessage
        {
            From = new MailAddress(
                settings.From,
                string.IsNullOrWhiteSpace(settings.FromDisplayName) ? "ApplyWise" : settings.FromDisplayName.Trim(),
                Encoding.UTF8),
            Subject = content.Subject,
            SubjectEncoding = Encoding.UTF8,
            Body = content.HtmlBody,
            BodyEncoding = Encoding.UTF8,
            IsBodyHtml = true
        };
        message.To.Add(new MailAddress(recipient));
        message.AlternateViews.Add(AlternateView.CreateAlternateViewFromString(
            content.TextBody,
            Encoding.UTF8,
            MediaTypeNames.Text.Plain));

        await client.SendMailAsync(message);
    }
}
