using ApplyWise.Web.Data;
using ApplyWise.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace ApplyWise.Web.Services.ResumeStorage;

public interface IResumeFileCleanupScheduler
{
    Task ScheduleAsync(string relativePath, CancellationToken cancellationToken = default);
}

public sealed class ResumeFileCleanupService(
    IServiceScopeFactory scopeFactory,
    IResumeStorageService storage,
    ILogger<ResumeFileCleanupService> logger)
    : BackgroundService, IResumeFileCleanupScheduler
{
    private const int BatchSize = 25;

    public async Task ScheduleAsync(
        string relativePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        if (await dbContext.ResumeFileCleanups.AnyAsync(
                cleanup => cleanup.FilePath == relativePath,
                cancellationToken))
        {
            return;
        }

        var now = DateTimeOffset.UtcNow;
        dbContext.ResumeFileCleanups.Add(new ResumeFileCleanup
        {
            FilePath = relativePath,
            CreatedAt = now,
            NextAttemptAt = now
        });
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var processedAny = false;
            try
            {
                processedAny = await ProcessBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "The durable resume-file cleanup worker failed a batch.");
            }

            try
            {
                await Task.Delay(
                    processedAny ? TimeSpan.FromSeconds(2) : TimeSpan.FromSeconds(30),
                    stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
        }
    }

    private async Task<bool> ProcessBatchAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var now = DateTimeOffset.UtcNow;
        var pending = await dbContext.ResumeFileCleanups
            .Where(cleanup => cleanup.NextAttemptAt <= now)
            .OrderBy(cleanup => cleanup.NextAttemptAt)
            .ThenBy(cleanup => cleanup.Id)
            .Take(BatchSize)
            .ToListAsync(cancellationToken);

        foreach (var cleanup in pending)
        {
            try
            {
                var absolutePath = storage.ResolvePath(cleanup.FilePath);
                if (File.Exists(absolutePath))
                {
                    File.Delete(absolutePath);
                }

                dbContext.ResumeFileCleanups.Remove(cleanup);
                await dbContext.SaveChangesAsync(cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                cleanup.AttemptCount++;
                cleanup.LastErrorType = exception.GetType().Name;
                cleanup.NextAttemptAt = DateTimeOffset.UtcNow.AddMinutes(
                    Math.Min(60, Math.Pow(2, Math.Min(cleanup.AttemptCount, 6))));
                await dbContext.SaveChangesAsync(cancellationToken);
                logger.LogWarning(
                    "A private resume file could not be removed; durable cleanup attempt {AttemptCount} is scheduled.",
                    cleanup.AttemptCount);
            }
        }

        return pending.Count > 0;
    }
}
