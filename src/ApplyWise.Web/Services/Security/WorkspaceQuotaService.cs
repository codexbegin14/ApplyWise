using ApplyWise.Web.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ApplyWise.Web.Services.Security;

public sealed class WorkspaceQuotaOptions
{
    public const string SectionName = "WorkspaceQuotas";

    public int MaxApplicationsPerUser { get; set; } = 1_000;
    public int MaxInterviewsPerUser { get; set; } = 1_000;
    public int MaxAnalysesPerUser { get; set; } = 2_000;
    public int MaxApplicationImportsPerUser { get; set; } = 2_000;
}

public interface IWorkspaceQuotaService
{
    Task<bool> CanCreateApplicationAsync(string userId, CancellationToken cancellationToken = default);
    Task<bool> CanCreateInterviewAsync(string userId, CancellationToken cancellationToken = default);
    Task<bool> CanCreateAnalysisAsync(string userId, CancellationToken cancellationToken = default);
    Task<bool> CanCreateApplicationImportAsync(string userId, CancellationToken cancellationToken = default);
}

public sealed class WorkspaceQuotaService(
    ApplicationDbContext db,
    IOptions<WorkspaceQuotaOptions> options) : IWorkspaceQuotaService
{
    private WorkspaceQuotaOptions Limits => options.Value;

    public async Task<bool> CanCreateApplicationAsync(string userId, CancellationToken cancellationToken = default) =>
        await db.JobApplications.CountAsync(item => item.UserId == userId, cancellationToken)
            < Limits.MaxApplicationsPerUser;

    public async Task<bool> CanCreateInterviewAsync(string userId, CancellationToken cancellationToken = default) =>
        await db.Interviews.CountAsync(item => item.UserId == userId, cancellationToken)
            < Limits.MaxInterviewsPerUser;

    public async Task<bool> CanCreateAnalysisAsync(string userId, CancellationToken cancellationToken = default) =>
        await db.ResumeAnalyses.CountAsync(item => item.UserId == userId, cancellationToken)
            < Limits.MaxAnalysesPerUser;

    public async Task<bool> CanCreateApplicationImportAsync(string userId, CancellationToken cancellationToken = default) =>
        await db.ApplicationImports.CountAsync(item => item.UserId == userId, cancellationToken)
            < Limits.MaxApplicationImportsPerUser;
}
