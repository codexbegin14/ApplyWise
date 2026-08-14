using ApplyWise.Web.Data;
using ApplyWise.Web.Services.Monitoring;
using ApplyWise.Web.ViewModels.Admin;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ApplyWise.Web.Services.Admin;

public interface IAdminDashboardService
{
    Task<AdminDashboardViewModel> LoadAsync(int days, CancellationToken cancellationToken = default);
}

public sealed class AdminDashboardService(
    ApplicationDbContext dbContext,
    TimeProvider timeProvider,
    IOptions<AdminAccessOptions> adminOptions) : IAdminDashboardService
{
    private static readonly IReadOnlyDictionary<string, string> EventLabels =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [ProductEventNames.AccountRegistered] = "Accounts registered",
            [ProductEventNames.EmailConfirmed] = "Emails confirmed",
            [ProductEventNames.LoginSucceeded] = "Successful logins",
            [ProductEventNames.OnboardingCompleted] = "Onboarding completed",
            [ProductEventNames.ResumeUploaded] = "Resumes uploaded",
            [ProductEventNames.ResumeAnalysisCompleted] = "Analyses completed",
            [ProductEventNames.ApplicationCreated] = "Applications created",
            [ProductEventNames.InterviewScheduled] = "Interviews scheduled",
            [ProductEventNames.ScamCheckCompleted] = "Scam checks completed"
        };

    public async Task<AdminDashboardViewModel> LoadAsync(
        int days,
        CancellationToken cancellationToken = default)
    {
        days = days is 7 or 30 or 90 ? days : 30;
        var now = timeProvider.GetUtcNow();
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var rangeStartDate = today.AddDays(-(days - 1));
        var rangeStart = new DateTimeOffset(
            rangeStartDate.ToDateTime(TimeOnly.MinValue),
            TimeSpan.Zero);
        var adminUserIds = await GetAdminUserIdsAsync(cancellationToken);

        var users = await (
            from user in dbContext.Users.AsNoTracking()
            where !adminUserIds.Contains(user.Id)
            join profile in dbContext.CareerProfiles.AsNoTracking()
                on user.Id equals profile.UserId into profiles
            from profile in profiles.DefaultIfEmpty()
            join activity in dbContext.UserAccountActivities.AsNoTracking()
                on user.Id equals activity.UserId into activities
            from activity in activities.DefaultIfEmpty()
            orderby activity != null ? activity.RegisteredAt : profile != null ? profile.CreatedAt : DateTimeOffset.MinValue descending
            select new
            {
                user.Id,
                Email = user.Email ?? user.UserName ?? "Unknown account",
                DisplayName = profile != null ? profile.FullName : string.Empty,
                user.EmailConfirmed,
                RegisteredAt = activity != null
                    ? activity.RegisteredAt
                    : profile != null ? profile.CreatedAt : DateTimeOffset.MinValue,
                LastLoginAt = activity != null ? activity.LastLoginAt : null,
                LastActivityAt = activity != null ? activity.LastActivityAt : null,
                SuccessfulLogins = activity != null ? activity.TotalSuccessfulLogins : 0,
                OnboardingCompleted = profile != null && profile.OnboardingCompletedAt != null,
                ResumeCount = dbContext.Resumes.Count(resume => resume.UserId == user.Id),
                AnalysisCount = dbContext.ResumeAnalyses.Count(analysis => analysis.UserId == user.Id),
                ApplicationCount = dbContext.JobApplications.Count(application => application.UserId == user.Id),
                InterviewCount = dbContext.Interviews.Count(interview => interview.UserId == user.Id)
            })
            .Take(100)
            .ToListAsync(cancellationToken);

        var eventRows = await dbContext.ProductEvents
            .AsNoTracking()
            .Where(productEvent => productEvent.OccurredAt >= rangeStart
                && (productEvent.UserId == null || !adminUserIds.Contains(productEvent.UserId)))
            .Select(productEvent => new
            {
                productEvent.Name,
                productEvent.UserId,
                productEvent.Succeeded,
                productEvent.OccurredAt
            })
            .ToListAsync(cancellationToken);

        var registrations = await dbContext.UserAccountActivities
            .AsNoTracking()
            .Where(activity => activity.RegisteredAt >= rangeStart
                && !adminUserIds.Contains(activity.UserId))
            .Select(activity => new { activity.UserId, activity.RegisteredAt })
            .ToListAsync(cancellationToken);

        var dailyActivity = Enumerable.Range(0, days)
            .Select(offset => rangeStartDate.AddDays(offset))
            .Select(date =>
            {
                var dayEvents = eventRows.Where(row =>
                    DateOnly.FromDateTime(row.OccurredAt.UtcDateTime) == date).ToArray();
                return new AdminDailyActivityViewModel(
                    date,
                    registrations.Count(row =>
                        DateOnly.FromDateTime(row.RegisteredAt.UtcDateTime) == date),
                    dayEvents.Where(row => row.UserId != null)
                        .Select(row => row.UserId)
                        .Distinct(StringComparer.Ordinal)
                        .Count(),
                    dayEvents.Length);
            })
            .ToArray();

        var featureUsage = eventRows
            .Where(row => EventLabels.ContainsKey(row.Name) && row.Succeeded)
            .GroupBy(row => row.Name, StringComparer.Ordinal)
            .Select(group => new AdminFeatureUsageViewModel(
                group.Key,
                EventLabels[group.Key],
                group.Count(),
                group.Where(row => row.UserId != null)
                    .Select(row => row.UserId)
                    .Distinct(StringComparer.Ordinal)
                    .Count()))
            .OrderByDescending(item => item.Count)
            .ToArray();

        return new AdminDashboardViewModel
        {
            GeneratedAt = now,
            Range = $"Last {days} days",
            TotalUsers = await dbContext.Users.CountAsync(
                user => !adminUserIds.Contains(user.Id),
                cancellationToken),
            ConfirmedUsers = await dbContext.Users.CountAsync(
                user => user.EmailConfirmed && !adminUserIds.Contains(user.Id),
                cancellationToken),
            NewUsersInRange = await dbContext.UserAccountActivities.CountAsync(
                activity => activity.RegisteredAt >= rangeStart
                    && !adminUserIds.Contains(activity.UserId),
                cancellationToken),
            ActiveUsersInRange = await dbContext.UserAccountActivities.CountAsync(
                activity => activity.LastActivityAt >= rangeStart
                    && !adminUserIds.Contains(activity.UserId),
                cancellationToken),
            CompletedOnboardingUsers = await dbContext.CareerProfiles.CountAsync(
                profile => profile.OnboardingCompletedAt != null
                    && !adminUserIds.Contains(profile.UserId),
                cancellationToken),
            TotalApplications = await dbContext.JobApplications.CountAsync(
                application => !adminUserIds.Contains(application.UserId),
                cancellationToken),
            TotalResumes = await dbContext.Resumes.CountAsync(
                resume => !adminUserIds.Contains(resume.UserId),
                cancellationToken),
            TotalAnalyses = await dbContext.ResumeAnalyses.CountAsync(
                analysis => !adminUserIds.Contains(analysis.UserId),
                cancellationToken),
            TotalInterviews = await dbContext.Interviews.CountAsync(
                interview => !adminUserIds.Contains(interview.UserId),
                cancellationToken),
            FailedEventsInRange = eventRows.Count(row => !row.Succeeded),
            Users = users.Select(user => new AdminUserRowViewModel(
                user.Email,
                string.IsNullOrWhiteSpace(user.DisplayName) ? "Not provided" : user.DisplayName,
                user.EmailConfirmed,
                user.RegisteredAt,
                user.LastLoginAt,
                user.LastActivityAt,
                user.SuccessfulLogins,
                user.ResumeCount,
                user.AnalysisCount,
                user.ApplicationCount,
                user.InterviewCount,
                user.OnboardingCompleted)).ToArray(),
            DailyActivity = dailyActivity,
            FeatureUsage = featureUsage
        };
    }

    private async Task<string[]> GetAdminUserIdsAsync(CancellationToken cancellationToken)
    {
        var roleUserIds = await (
            from userRole in dbContext.UserRoles.AsNoTracking()
            join role in dbContext.Roles.AsNoTracking()
                on userRole.RoleId equals role.Id
            where role.NormalizedName == AdminAccess.Role.ToUpperInvariant()
            select userRole.UserId)
            .ToListAsync(cancellationToken);

        var configuredEmails = adminOptions.Value.ValidEmails()
            .Select(email => email.ToUpperInvariant())
            .ToArray();
        if (configuredEmails.Length > 0)
        {
            roleUserIds.AddRange(await dbContext.Users
                .AsNoTracking()
                .Where(user => user.NormalizedEmail != null
                    && configuredEmails.Contains(user.NormalizedEmail))
                .Select(user => user.Id)
                .ToListAsync(cancellationToken));
        }

        return roleUserIds.Distinct(StringComparer.Ordinal).ToArray();
    }
}
