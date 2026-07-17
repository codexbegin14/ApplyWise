using System.Text.Json;
using ApplyWise.Web.Data;
using ApplyWise.Web.Models;
using ApplyWise.Web.Services.ResumeAnalysis;
using Microsoft.EntityFrameworkCore;

namespace ApplyWise.Web.Services.Analytics;

public sealed class AnalyticsService(ApplicationDbContext dbContext) : IAnalyticsService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

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

        var analysisRows = await dbContext.ResumeAnalyses.AsNoTracking()
            .Where(analysis => analysis.UserId == userId)
            .OrderByDescending(analysis => analysis.CreatedAt)
            .Select(analysis => new
            {
                analysis.Id,
                ResumeVersionName = analysis.Resume!.VersionName,
                CompanyName = analysis.JobApplication != null ? analysis.JobApplication.CompanyName : "Direct input",
                JobTitle = analysis.JobApplication != null ? analysis.JobApplication.JobTitle : "Pasted requirements",
                analysis.MatchScore,
                analysis.AtsReadinessScore,
                analysis.JobMatchScore,
                analysis.ScoreVersion,
                analysis.WarningsJson,
                analysis.CreatedAt
            })
            .ToListAsync(cancellationToken);
        var analyses = analysisRows.Select(analysis => new RecentAnalysisItem(
            analysis.Id,
            analysis.ResumeVersionName,
            analysis.CompanyName,
            analysis.JobTitle,
            analysis.MatchScore,
            analysis.AtsReadinessScore,
            analysis.JobMatchScore,
            analysis.ScoreVersion ?? "legacy-v1",
            analysis.CreatedAt)).ToArray();
        var currentAnalyses = analysisRows
            .Where(analysis => analysis.ScoreVersion == ResumeAnalysisResult.CurrentScoreVersion)
            .ToArray();
        var fitAnalyses = currentAnalyses.Where(analysis => analysis.JobMatchScore.HasValue).ToArray();
        var mostFrequentWarning = currentAnalyses
            .SelectMany(analysis => DeserializeWarnings(analysis.WarningsJson))
            .Where(warning => warning.Code is AnalysisWarningCode.UnreadableText or AnalysisWarningCode.LimitedText or AnalysisWarningCode.ParsingOrder)
            .GroupBy(warning => warning.Message, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.Key)
            .FirstOrDefault();

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
            applications.Count(application => application.Status == ApplicationStatus.Offered),
            applications.Count(application => application.Status == ApplicationStatus.Rejected),
            fitAnalyses.Length == 0 ? 0 : Math.Round(fitAnalyses.Average(analysis => analysis.MatchScore), 1),
            currentAnalyses.Length == 0 ? 0 : Math.Round(currentAnalyses.Average(analysis => analysis.AtsReadinessScore ?? 0), 1),
            currentAnalyses.Length,
            analysisRows.Count - currentAnalyses.Length,
            mostFrequentWarning,
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
        var query = dbContext.ResumeAnalyses.AsNoTracking().Where(analysis =>
            analysis.UserId == userId && analysis.ScoreVersion == ResumeAnalysisResult.CurrentScoreVersion);
        if (since.HasValue) query = query.Where(analysis => analysis.CreatedAt >= since.Value);

        var rows = await query.Select(analysis => new
        {
            analysis.JobApplicationId,
            analysis.MissingKeywordsJson,
            analysis.ReviewJson
        }).ToListAsync(cancellationToken);

        var occurrences = rows
            .SelectMany(row =>
            {
                var requirements = DeserializeMissingRequirements(row.ReviewJson)
                    .Where(IsSkillRequirement)
                    .GroupBy(item => item.Name, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.OrderBy(item => PriorityRank(item.Priority)).First())
                    .ToArray();
                return requirements.Length > 0
                    ? requirements.Select(item => new MissingOccurrence(item.Name, item.Priority, row.JobApplicationId))
                    : Deserialize(row.MissingKeywordsJson).Distinct(StringComparer.OrdinalIgnoreCase)
                        .Select(skill => new MissingOccurrence(skill, RequirementPriority.Informational, row.JobApplicationId));
            })
            .GroupBy(item => item.Skill, StringComparer.OrdinalIgnoreCase)
            .Select(group => new
            {
                Skill = group.Key,
                Count = group.Count(),
                PriorityRank = group.Min(item => PriorityRank(item.Priority)),
                JobCount = group.Select(item => item.JobApplicationId)
                    .Where(jobApplicationId => jobApplicationId.HasValue)
                    .Distinct()
                    .Count()
            })
            .OrderBy(item => item.PriorityRank)
            .ThenByDescending(item => item.Count)
            .ThenBy(item => item.Skill)
            .ToArray();

        return occurrences.Select(item => new SkillGapTrendItem(
            item.Skill,
            item.Count,
            item.JobCount,
            item.PriorityRank switch { 0 => "Critical", 1 => "High", 2 => "Medium", _ => "Low" },
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
            .Where(analysis => analysis.UserId == userId
                && analysis.ScoreVersion == ResumeAnalysisResult.CurrentScoreVersion)
            .Select(analysis => new
            {
                analysis.ResumeId,
                analysis.MatchScore,
                analysis.AtsReadinessScore,
                analysis.JobMatchScore,
                analysis.EvidenceQuality,
                analysis.CreatedAt
            })
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
            var fitAnalyses = resumeAnalyses.Where(analysis => analysis.JobMatchScore.HasValue).ToArray();
            var linkedApplications = applications.Where(application => application.ResumeId == resume.Id).ToArray();
            var interviews = linkedApplications.Count(application => interviewedSet.Contains(application.Id));
            return new
            {
                resume.Id,
                resume.VersionName,
                Average = fitAnalyses.Length == 0 ? 0 : Math.Round(fitAnalyses.Average(item => item.MatchScore), 1),
                AverageAts = resumeAnalyses.Length == 0 ? 0 : Math.Round(resumeAnalyses.Average(item => item.AtsReadinessScore ?? 0), 1),
                AverageEvidence = fitAnalyses.Length == 0 ? 0 : Math.Round(fitAnalyses.Average(item => (item.EvidenceQuality ?? 0) * 100), 1),
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
                item.Id, item.VersionName, item.Average, item.AverageAts, item.AverageEvidence, item.AnalysisCount, item.Applications,
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
        return new ApplicationFunnelResult(
            rows.Count(row => row.Status == ApplicationStatus.Applied),
            rows.Count(row => row.Status == ApplicationStatus.Pending),
            rows.Count(row => interviewedApplicationIds.Contains(row.Id)
                || row.Status == ApplicationStatus.Interview),
            rows.Count(row => row.Status == ApplicationStatus.Offered),
            rows.Count(row => row.Status == ApplicationStatus.Accepted),
            rows.Count(row => row.Status == ApplicationStatus.Rejected),
            rows.Count(row => row.Status == ApplicationStatus.UserRejected),
            rows.Count(row => row.Status == ApplicationStatus.Ignored));
    }

    private static string BuildSkillAction(string skill) => skill switch
    {
        "SQL Server" => "If relevant, strengthen a project bullet with database design, queries, or EF Core usage.",
        "ASP.NET Core" => "If you have experience, show where you used ASP.NET Core in a concrete project or role.",
        "Azure" or "AWS" => $"If relevant, document hands-on {skill} services, deployment, or hosting experience.",
        "Unit Testing" => "If you have written automated tests, name the framework and the behavior you verified.",
        _ => $"If you have genuine {skill} experience, add a specific example showing where and how you used it."
    };

    private static int PriorityRank(RequirementPriority priority) => priority switch
    {
        RequirementPriority.MustHave => 0,
        RequirementPriority.Required => 1,
        RequirementPriority.Preferred => 2,
        _ => 3
    };

    private static bool IsSkillRequirement(JobRequirement requirement) => requirement.Category is
        RequirementCategory.TechnicalSkill or RequirementCategory.Tool or RequirementCategory.DomainSkill or
        RequirementCategory.SoftSkill or RequirementCategory.Language;

    private static IReadOnlyList<JobRequirement> DeserializeMissingRequirements(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return JsonSerializer.Deserialize<AnalyticsReviewPayload>(json, JsonOptions)?.MissingRequirements ?? []; }
        catch (JsonException) { return []; }
    }

    private static IReadOnlyList<AnalysisWarning> DeserializeWarnings(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try { return JsonSerializer.Deserialize<AnalysisWarning[]>(json, JsonOptions) ?? []; }
        catch (JsonException) { return []; }
    }

    private static IReadOnlyList<string> Deserialize(string json)
    {
        try { return JsonSerializer.Deserialize<string[]>(json, JsonOptions) ?? []; }
        catch (JsonException) { return []; }
    }

    private sealed record MissingOccurrence(string Skill, RequirementPriority Priority, int? JobApplicationId);
    private sealed class AnalyticsReviewPayload
    {
        public JobRequirement[] MissingRequirements { get; init; } = [];
    }
}
