using System.Threading.Channels;
using ApplyWise.Web.Models;
using Microsoft.AspNetCore.Identity;

namespace ApplyWise.Web.Services.AccountSecurity;

public interface IAccountSecurityRequestQueue
{
    bool TryQueue(string email, AccountSecurityAction action);
}

public sealed class AccountSecurityRequestQueue(
    IServiceScopeFactory scopeFactory,
    ILogger<AccountSecurityRequestQueue> logger)
    : BackgroundService, IAccountSecurityRequestQueue
{
    private const int Capacity = 256;
    private readonly Channel<AccountSecurityRequest> _requests =
        Channel.CreateBounded<AccountSecurityRequest>(new BoundedChannelOptions(Capacity)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait
        });

    public bool TryQueue(string email, AccountSecurityAction action)
    {
        if (action is not (AccountSecurityAction.ConfirmEmail or AccountSecurityAction.ResetPassword))
        {
            throw new ArgumentOutOfRangeException(nameof(action));
        }

        return _requests.Writer.TryWrite(new AccountSecurityRequest(email.Trim(), action));
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var request in _requests.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var userManager = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
                var securityCodes = scope.ServiceProvider.GetRequiredService<IAccountSecurityCodeService>();
                var user = await userManager.FindByEmailAsync(request.Email);
                if (user is null)
                {
                    continue;
                }

                var isConfirmed = await userManager.IsEmailConfirmedAsync(user);
                var isEligible = request.Action switch
                {
                    AccountSecurityAction.ConfirmEmail => !isConfirmed,
                    AccountSecurityAction.ResetPassword => isConfirmed,
                    _ => false
                };
                if (!isEligible)
                {
                    continue;
                }

                await securityCodes.IssueAsync(
                    user.Id,
                    request.Email,
                    request.Action,
                    stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "An anonymous account-security delivery request could not be completed.");
            }
        }
    }

    private sealed record AccountSecurityRequest(string Email, AccountSecurityAction Action);
}
