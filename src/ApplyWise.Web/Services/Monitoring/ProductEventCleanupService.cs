using ApplyWise.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ApplyWise.Web.Services.Monitoring;

public sealed class ProductEventRetentionOptions
{
    public const string SectionName = "ProductEvents";
    public int RetentionDays { get; set; } = 90;
}

public sealed class ProductEventCleanupService(
    IServiceScopeFactory scopeFactory,
    IOptions<ProductEventRetentionOptions> options,
    TimeProvider timeProvider,
    ILogger<ProductEventCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RemoveExpiredEventsAsync(stoppingToken);
        using var timer = new PeriodicTimer(TimeSpan.FromHours(24));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RemoveExpiredEventsAsync(stoppingToken);
        }
    }

    private async Task RemoveExpiredEventsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var cutoff = timeProvider.GetUtcNow().AddDays(-options.Value.RetentionDays);
            var deleted = await dbContext.ProductEvents
                .Where(productEvent => productEvent.OccurredAt < cutoff)
                .ExecuteDeleteAsync(cancellationToken);
            if (deleted > 0)
            {
                logger.LogInformation("Removed {EventCount} expired product events.", deleted);
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(exception, "Could not remove expired product events.");
        }
    }
}
