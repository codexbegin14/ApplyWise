using System.Text.Json;
using ApplyWise.Web.Data;
using ApplyWise.Web.Models;
using Microsoft.EntityFrameworkCore;

namespace ApplyWise.Web.Services.Analytics;

public sealed class AnalyticsService(ApplicationDbContext dbContext) : IAnalyticsService
{
    public async Task<AnalyticsOverviewResult> GetOverviewAsync(
        string userId, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var applications = await dbContext.JobApplications.AsNoTracking()
            .Where(application => application.UserId == userId)
            .Select(application => new { application.Id, application.CompanyName, application.JobTitle, application.Status, application.CreatedAt })
            .ToListAsync(cancellationToken);

        var interviewRows = await dbContext.Interviews.AsNoTracking()
            .Where(interview => interview.UserId == userId)
            .Select(interview => new { interview.JobApplicationId, interview.ScheduledAt, interview.Status })
            .ToListAsync(cancellationToken);

        var analyses = await dbContext.ResumeAnalyses.AsNoTracking()
            .Where(analysis => analysis.UserId == userId)
            .OrderByDescending(analysis => analysis.CreatedAt)
            .Select(analysis => new RecentAnalysisItem(
                analysis.Id,
                analysis.Resume!.VersionName,
                analysis.JobApplication!.CompanyName,
                analysis.JobApplication.JobTitle,
                analysis.MatchScore,
                analysis.CreatedAt))
            .ToListAsync(cancellationToken);

        var pendingReminderCount = await dbContext.Reminders.CountAsync(
            reminder => reminder.UserId == userId && !reminder.IsCompleted, cancellationToken);
        var overdueReminderCount = await dbContext.Reminders.CountAsync(
            reminder => reminder.UserId == userId && !reminder.IsCompleted && reminder.DueAt < now, cancellationToken);

        var statusCounts = Enum.GetValues<ApplicationStatus>()
            .Select(status => new StatusCountItem(status, applications.Count(application => application.Status == status)))
            .ToArray();
        var interviewedApplicationIds = interviewRows.Select(interview => interview.JobApplicationId).ToHashSet();
        var funnel = BuildFunnel(applications.Select(application => (application.Id, application.Status)), interviewedApplicationIds);
        var total = applications.Count;

        return new AnalyticsOverviewResult(
            total,
            interviewRows.Count,
            applications.Count(application => application.Status == ApplicationStatus.Offer),
            applications.Count(application => application.Status == ApplicationStatus.Rejected),
            analyses.Count == 0 ? 0 : Math.Round(analyses.Average(analysis => analysis.MatchScore), 1),
            pendingReminderCount,
            overdueReminderCount,
            interviewRows.Count(interview => interview.ScheduledAt >= now
                && interview.Status is InterviewStatus.Scheduled or InterviewStatus.Rescheduled),
            total == 0 ? 0 : Math.Round(interviewedApplicationIds.Count * 100d / total, 1),
            statusCounts,
            funnel,
            applications.OrderByDescending(application => application.CreatedAt).Take(5)
                .Select(application => new RecentApplicationItem(
                    application.Id, application.CompanyName, application.JobTitle, application.Status, application.CreatedAt))
                .ToArray(),
            analyses.Take(5).ToArray());
    }

    public async Task<IReadOnlyList<SkillGapTrendItem>> GetSkillGapTrendsAsync(
        string userId, DateTimeOffset? since = null, CancellationToken cancellationToken = default)
    {
        var query = dbContext.ResumeAnalyses.AsNoTracking().Where(analysis => analysis.UserId == userId);
        if (since.HasValue) query = query.Where(analysis => analysis.CreatedAt >= since.Value);

        var rows = await query.Select(analysis => new
        {
            analysis.JobApplicationId,
            analysis.MissingKeywordsJson
        }).ToListAsync(cancellationToken);

        var occurrences = rows
            .SelectMany(row => Deserialize(row.MissingKeywordsJson)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(skill => new { Skill = skill, row.JobApplicationId }))
            .GroupBy(item => item.Skill, StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                Skill = group.Key,
                Count = group.Count(),
                JobCount = group.Select(item => item.JobApplicationId).Distinct().Count()
            })
            .OrderByDescending(item => item.Count)
            .ThenBy(item => item.Skill)
            .ToArray();

        return occurrences.Select(item => new SkillGapTrendItem(
            item.Skill,
            item.Count,
            item.JobCount,
            item.Count >= 3 ? "High" : item.Count == 2 ? "Medium" : "Low",
            BuildSkillAction(item.Skill))).ToArray();
    }

    public async Task<IReadOnlyList<ResumePerformanceItem>> GetResumePerformanceAsync(
        string userId, CancellationToken cancellationToken = default)
    {
        var resumes = await dbContext.Resumes.AsNoTracking()
            .Where(resume => resume.UserId == userId)
            .Select(resume => new { resume.Id, resume.VersionName })
            .ToListAsync(cancellationToken);
        var analyses = await dbContext.ResumeAnalyses.AsNoTracking()
            .Where(analysis => analysis.UserId == userId)
            .Select(analysis => new { analysis.ResumeId, analysis.MatchScore, analysis.CreatedAt })
            .ToListAsync(cancellationToken);
        var applications = await dbContext.JobApplications.AsNoTracking()
            .Where(application => application.UserId == userId)
            .Select(application => new { application.Id, application.ResumeId })
            .ToListAsync(cancellationToken);
        var interviewedJobIds = await dbContext.Interviews.AsNoTracking()
            .Where(interview => interview.UserId == userId)
            .Select(interview => interview.JobApplicationId)
            .Distinct()
            .ToListAsync(cancellationToken);
        var interviewedSet = interviewedJobIds.ToHashSet();

        var raw = resumes.Select(resume =>
        {
            var resumeAnalyses = analyses.Where(analysis => analysis.ResumeId == resume.Id).ToArray();
            var linkedApplications = applications.Where(application => application.ResumeId == resume.Id).ToArray();
            var interviews = linkedApplications.Count(application => interviewedSet.Contains(application.Id));
            return new
            {
                resume.Id,
                resume.VersionName,
                Average = resumeAnalyses.Length == 0 ? 0 : Math.Round(resumeAnalyses.Average(item => item.MatchScore), 1),
                AnalysisCount = resumeAnalyses.Length,
                Applications = linkedApplications.Length,
                Interviews = interviews,
                Rate = linkedApplications.Length == 0 ? 0 : Math.Round(interviews * 100d / linkedApplications.Length, 1),
                Last = resumeAnalyses.Select(item => (DateTimeOffset?)item.CreatedAt).Max()
            };
        }).ToArray();

        var bestId = raw.Any(item => item.AnalysisCount > 0 || item.Interviews > 0)
            ? raw.OrderByDescending(item => item.Rate).ThenByDescending(item => item.Average)
                .ThenByDescending(item => item.Interviews).Select(item => (int?)item.Id).FirstOrDefault()
            : null;
        var mostUsedId = raw.Any(item => item.Applications > 0)
            ? raw.OrderByDescending(item => item.Applications).ThenByDescending(item => item.AnalysisCount)
                .Select(item => (int?)item.Id).FirstOrDefault()
            : null;

        return raw.OrderByDescending(item => item.Rate).ThenByDescending(item => item.Average)
            .Select(item => new ResumePerformanceItem(
                item.Id, item.VersionName, item.Average, item.AnalysisCount, item.Applications,
                item.Interviews, item.Rate, item.Last, item.Id == bestId, item.Id == mostUsedId)).ToArray();
    }

    public async Task<IReadOnlyList<PlatformAnalyticsItem>> GetPlatformAnalyticsAsync(
        string userId, CancellationToken cancellationToken = default)
    {
        var applications = await dbContext.JobApplications.AsNoTracking()
            .Where(application => application.UserId == userId)
            .Select(application => new { application.Id, application.Source })
            .ToListAsync(cancellationToken);
        var interviewedIds = (await dbContext.Interviews.AsNoTracking()
            .Where(interview => interview.UserId == userId)
            .Select(interview => interview.JobApplicationId)
            .Distinct().ToListAsync(cancellationToken)).ToHashSet();

        var raw = applications.GroupBy(application => application.Source).Select(group =>
        {
            var interviewCount = group.Count(application => interviewedIds.Contains(application.Id));
            return new
            {
                Source = group.Key,
                Count = group.Count(),
                Interviews = interviewCount,
                Rate = Math.Round(interviewCount * 100d / group.Count(), 1)
            };
        }).OrderByDescending(item => item.Rate).ThenByDescending(item => item.Interviews)
            .ThenByDescending(item => item.Count).ToArray();
        var bestSource = raw.Any(item => item.Interviews > 0)
            ? raw.Select(item => (JobSource?)item.Source).FirstOrDefault()
            : null;

        return raw.Select(item => new PlatformAnalyticsItem(
            item.Source, item.Count, item.Interviews, item.Rate, item.Source == bestSource)).ToArray();
    }

    private static ApplicationFunnelResult BuildFunnel(
        IEnumerable<(int Id, ApplicationStatus Status)> applications,
        IReadOnlySet<int> interviewedApplicationIds)
    {
        var rows = applications.ToArray();
        var appliedStatuses = new HashSet<ApplicationStatus>
        {
            ApplicationStatus.Applied, ApplicationStatus.Shortlisted, ApplicationStatus.Interview,
            ApplicationStatus.TechnicalTest, ApplicationStatus.Offer, ApplicationStatus.Rejected
        };
        var shortlistedStatuses = new HashSet<ApplicationStatus>
        {
            ApplicationStatus.Shortlisted, ApplicationStatus.Interview,
            ApplicationStatus.TechnicalTest, ApplicationStatus.Offer
        };
        return new ApplicationFunnelResult(
            rows.Length,
            rows.Count(row => appliedStatuses.Contains(row.Status)),
            rows.Count(row => shortlistedStatuses.Contains(row.Status)),
            rows.Count(row => interviewedApplicationIds.Contains(row.Id)
                || row.Status is ApplicationStatus.Interview or ApplicationStatus.Offer),
            rows.Count(row => row.Status == ApplicationStatus.Offer),
            rows.Count(row => row.Status == ApplicationStatus.Rejected));
    }

    private static string BuildSkillAction(string skill) => skill switch
    {
        "SQL Server" => "If relevant, strengthen a project bullet with database design, queries, or EF Core usage.",
        "ASP.NET Core" => "If you have experience, show where you used ASP.NET Core in a concrete project or role.",
        "Azure" or "AWS" => $"If relevant, document hands-on {skill} services, deployment, or hosting experience.",
        "Unit Testing" => "If you have written automated tests, name the framework and the behavior you verified.",
        _ => $"If you have genuine {skill} experience, add a specific example showing where and how you used it."
    };

    private static IReadOnlyList<string> Deserialize(string json)
    {
        try { return JsonSerializer.Deserialize<string[]>(json) ?? []; }
        catch (JsonException) { return []; }
    }
}
