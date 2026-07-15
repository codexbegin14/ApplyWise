using System.Net;
using System.Net.Mail;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace ApplyWise.Web.Services.Email;

public sealed class SmtpEmailSender(IOptions<EmailOptions> options, IWebHostEnvironment environment,
    ILogger<SmtpEmailSender> logger) : IEmailSender<IdentityUser>, IApplicationEmailSender
{
    private readonly EmailOptions settings = options.Value;

    public Task SendConfirmationLinkAsync(IdentityUser user, string email, string confirmationLink) =>
        SendAsync(email, "Confirm your ApplyWise email", $"Confirm your ApplyWise account: {confirmationLink}");

    public Task SendPasswordResetLinkAsync(IdentityUser user, string email, string resetLink) =>
        SendAsync(email, "Reset your ApplyWise password", $"Reset your ApplyWise password: {resetLink}");

    public Task SendPasswordResetCodeAsync(IdentityUser user, string email, string resetCode) =>
        SendAsync(email, "Your ApplyWise password reset code", $"Your ApplyWise reset code is {resetCode}.");

    public Task SendAccountSecurityCodeAsync(string email, string actionLabel, string code) =>
        SendAsync(email, $"Your ApplyWise confirmation code", $"Use this code to {actionLabel}: {code}. It expires in 10 minutes. If you did not request this, you can safely ignore this email.");

    private async Task SendAsync(string recipient, string subject, string body)
    {
        if (string.IsNullOrWhiteSpace(settings.Host) || string.IsNullOrWhiteSpace(settings.From))
        {
            if (environment.IsDevelopment())
            {
                logger.LogInformation("Development email to {Recipient}: {Subject}. Body: {Body}", recipient, subject, body);
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
        using var message = new MailMessage(settings.From, recipient, subject, body);
        await client.SendMailAsync(message);
    }
}
