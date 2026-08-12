using System.Security.Claims;
using ApplyWise.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace ApplyWise.Web.Services.Monitoring;

public sealed class UserActivityMiddleware(
    RequestDelegate next,
    IMemoryCache cache,
    TimeProvider timeProvider,
    ILogger<UserActivityMiddleware> logger)
{
    private static readonly TimeSpan WriteInterval = TimeSpan.FromMinutes(5);

    public async Task InvokeAsync(HttpContext context, ApplicationDbContext dbContext)
    {
        await next(context);

        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId) || context.Response.StatusCode >= 500)
        {
            return;
        }

        var cacheKey = $"activity:{userId}";
        if (cache.TryGetValue(cacheKey, out _))
        {
            return;
        }

        cache.Set(cacheKey, true, WriteInterval);
        try
        {
            var now = timeProvider.GetUtcNow();
            await dbContext.UserAccountActivities
                .Where(activity => activity.UserId == userId)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(activity => activity.LastActivityAt, now),
                    context.RequestAborted);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            cache.Remove(cacheKey);
            logger.LogWarning(exception, "Could not update the activity timestamp for user {UserId}.", userId);
        }
    }
}
