using System.Security.Claims;
using ApplyWise.Web.Data;
using ApplyWise.Web.Models;
using ApplyWise.Web.Services.Gmail;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ApplyWise.Web.Controllers;

[Authorize]
[Route("connections/gmail")]
public sealed class GmailConnectionsController(
    ApplicationDbContext dbContext,
    UserManager<IdentityUser> userManager,
    IGmailCredentialProtector credentialProtector,
    IGmailImportService gmailImportService,
    IHttpClientFactory httpClientFactory,
    IOptions<GoogleIntegrationOptions> googleOptions,
    ILogger<GmailConnectionsController> logger) : Controller
{
    [HttpGet("failure")]
    public IActionResult Failure()
    {
        TempData["ImportError"] =
            "Gmail authorization was cancelled, expired, or opened without a valid connection request. Start again from the Imports page.";
        return RedirectToAction("Index", "ApplicationImports");
    }

    [HttpPost("connect")]
    [ValidateAntiForgeryToken]
    public IActionResult Connect()
    {
        if (!googleOptions.Value.IsConfigured)
        {
            TempData["ImportError"] =
                "Google integration is not configured on this ApplyWise deployment.";
            return RedirectToAction("Index", "ApplicationImports");
        }

        var properties = new AuthenticationProperties
        {
            RedirectUri = Url.Action(nameof(Callback))
        };
        return Challenge(properties, GmailAuthenticationDefaults.Scheme);
    }

    [HttpGet("callback")]
    public async Task<IActionResult> Callback()
    {
        var userId = userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId)) return Challenge();

        var result = await HttpContext.AuthenticateAsync(IdentityConstants.ExternalScheme);
        try
        {
            if (!result.Succeeded
                || result.Principal is null
                || result.Properties is null
                || !result.Principal.HasClaim(
                    GmailAuthenticationDefaults.FlowClaimType,
                    GmailAuthenticationDefaults.FlowClaimValue))
            {
                TempData["ImportError"] =
                    "Gmail authorization could not be verified. Please try connecting again.";
                return RedirectToAction("Index", "ApplicationImports");
            }

            var email = result.Principal.FindFirstValue(ClaimTypes.Email)?.Trim();
            var refreshToken = result.Properties.GetTokenValue("refresh_token");
            var connection = await dbContext.GmailConnections
                .SingleOrDefaultAsync(
                    item => item.UserId == userId,
                    HttpContext.RequestAborted);

            if (string.IsNullOrWhiteSpace(email))
            {
                TempData["ImportError"] =
                    "Google did not provide the Gmail address for this connection.";
                return RedirectToAction("Index", "ApplicationImports");
            }

            if (string.IsNullOrWhiteSpace(refreshToken) && connection is null)
            {
                TempData["ImportError"] =
                    "Google did not provide offline access. Revoke ApplyWise in your Google Account and connect again.";
                return RedirectToAction("Index", "ApplicationImports");
            }

            var now = DateTimeOffset.UtcNow;
            if (connection is null)
            {
                connection = new GmailConnection
                {
                    UserId = userId,
                    EmailAddress = email,
                    ProtectedRefreshToken = credentialProtector.Protect(refreshToken!),
                    ConnectedAt = now,
                    UpdatedAt = now,
                    NextSyncAt = now
                };
                dbContext.GmailConnections.Add(connection);
            }
            else
            {
                connection.EmailAddress = email;
                if (!string.IsNullOrWhiteSpace(refreshToken))
                {
                    connection.ProtectedRefreshToken =
                        credentialProtector.Protect(refreshToken);
                }
                connection.UpdatedAt = now;
                connection.NextSyncAt = now;
                connection.LastErrorCode = null;
            }

            await dbContext.SaveChangesAsync(HttpContext.RequestAborted);
            var syncResult = await gmailImportService.SyncUserAsync(
                userId,
                HttpContext.RequestAborted);
            TempData[syncResult.Succeeded ? "ImportSuccess" : "ImportError"] =
                syncResult.Message;
            return RedirectToAction("Index", "ApplicationImports");
        }
        finally
        {
            await HttpContext.SignOutAsync(IdentityConstants.ExternalScheme);
        }
    }

    [HttpPost("disconnect")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Disconnect()
    {
        var userId = userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(userId)) return Challenge();
        var connection = await dbContext.GmailConnections
            .SingleOrDefaultAsync(
                item => item.UserId == userId,
                HttpContext.RequestAborted);
        if (connection is null)
        {
            return RedirectToAction("Index", "ApplicationImports");
        }

        try
        {
            var token = credentialProtector.Unprotect(connection.ProtectedRefreshToken);
            var client = httpClientFactory.CreateClient("GoogleOAuth");
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                "https://oauth2.googleapis.com/revoke")
            {
                Content = new FormUrlEncodedContent(
                    new Dictionary<string, string> { ["token"] = token })
            };
            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                HttpContext.RequestAborted);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning(
                    "Google token revocation returned {StatusCode} for Gmail connection {ConnectionId}.",
                    (int)response.StatusCode,
                    connection.Id);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(
                exception,
                "Google token revocation could not be completed for Gmail connection {ConnectionId}.",
                connection.Id);
        }

        dbContext.GmailConnections.Remove(connection);
        await dbContext.SaveChangesAsync(HttpContext.RequestAborted);
        TempData["ImportSuccess"] =
            "Gmail was disconnected and pending email imports were removed. Accepted applications were kept.";
        return RedirectToAction("Index", "ApplicationImports");
    }
}
