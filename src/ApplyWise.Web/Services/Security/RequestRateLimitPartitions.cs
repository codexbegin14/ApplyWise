using System.Security.Claims;
using System.Threading.RateLimiting;

namespace ApplyWise.Web.Services.Security;

public static class RequestRateLimitPartitions
{
    private const int AccountSecurityPermitLimit = 8;

    public static RateLimitPartition<string> CreateGlobal(
        HttpContext context,
        int permitLimit)
    {
        var clientKey = GetClientKey(context);
        if (IsReadOnly(context.Request.Method) &&
            (context.Request.Path.StartsWithSegments("/health") ||
             Path.HasExtension(context.Request.Path.Value)))
        {
            return RateLimitPartition.GetNoLimiter($"global-read:{clientKey}");
        }

        return RateLimitPartition.GetFixedWindowLimiter(
            $"global:{clientKey}",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
                AutoReplenishment = true
            });
    }

    public static RateLimitPartition<string> CreateAccountSecurity(HttpContext context)
    {
        var clientKey = GetClientKey(context);
        if (IsReadOnly(context.Request.Method))
        {
            return RateLimitPartition.GetNoLimiter($"account-read:{clientKey}");
        }

        return RateLimitPartition.GetFixedWindowLimiter(
            $"account-write:{clientKey}",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = AccountSecurityPermitLimit,
                Window = TimeSpan.FromMinutes(10),
                QueueLimit = 0,
                AutoReplenishment = true
            });
    }

    private static string GetClientKey(HttpContext context) =>
        context.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
        ?? context.Connection.RemoteIpAddress?.ToString()
        ?? "unknown";

    private static bool IsReadOnly(string method) =>
        HttpMethods.IsGet(method) ||
        HttpMethods.IsHead(method) ||
        HttpMethods.IsOptions(method);
}
