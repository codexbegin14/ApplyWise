using ApplyWise.Web.Data;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ApplyWise.Web.Services.Health;

public sealed class DatabaseHealthCheck(ApplicationDbContext db) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try { return await db.Database.CanConnectAsync(cancellationToken) ? HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("Database is unavailable."); }
        catch (Exception exception) { return HealthCheckResult.Unhealthy("Database check failed.", exception); }
    }
}
