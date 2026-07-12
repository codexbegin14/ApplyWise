using ApplyWise.Web.Data;
using ApplyWise.Web.Models;
using ApplyWise.Web.Services.Analytics;
using ApplyWise.Web.ViewModels.Dashboard;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ApplyWise.Web.Controllers;

[Authorize]
public class DashboardController(
    ApplicationDbContext dbContext,
    UserManager<IdentityUser> userManager,
    IAnalyticsService analyticsService) : Controller
{
    public async Task<IActionResult> Index()
    {
        var userId = userManager.GetUserId(User)
            ?? throw new InvalidOperationException("The current user does not have an identifier.");
        var now = DateTimeOffset.UtcNow;
        var localNow = DateTimeOffset.Now;
        var today = DateOnly.FromDateTime(localNow.DateTime);
        var todayStart = new DateTimeOffset(localNow.Date, localNow.Offset).ToUniversalTime();
        var tomorrowStart = todayStart.AddDays(1);
        var analytics = await analyticsService.GetOverviewAsync(userId, HttpContext.RequestAborted);
        var resumePerformance = await analyticsService.GetResumePerformanceAsync(userId, HttpContext.RequestAborted);
        var bestResume = resumePerformance.FirstOrDefault(resume => resume.IsBestPerforming)
            ?? resumePerformance.OrderByDescending(resume => resume.AverageMatchScore).FirstOrDefault();

        var model = new DashboardViewModel
        {
            CurrentTime = localNow,
            TotalApplications = analytics.TotalApplications,
            TotalInterviewCount = analytics.InterviewCount,
            AverageMatchScore = analytics.AverageMatchScore,
            UpcomingInterviewCount = await dbContext.Interviews.CountAsync(interview =>
                interview.UserId == userId && interview.ScheduledAt >= now
                && (interview.Status == InterviewStatus.Scheduled || interview.Status == InterviewStatus.Rescheduled)),
            PendingReminderCount = await dbContext.Reminders.CountAsync(reminder =>
                reminder.UserId == userId && !reminder.IsCompleted),
            OverdueReminderCount = await dbContext.Reminders.CountAsync(reminder =>
                reminder.UserId == userId && !reminder.IsCompleted && reminder.DueAt < now),
            Funnel = analytics.Funnel,
            BestResumeVersionName = bestResume?.VersionName,
            BestResumeScore = bestResume?.AverageMatchScore ?? 0,
            RecentApplications = analytics.RecentApplications,
            RecentAnalyses = analytics.RecentAnalyses
        };
        model.TopSkillGaps = (await analyticsService.GetSkillGapTrendsAsync(
            userId, cancellationToken: HttpContext.RequestAborted)).Take(4).ToArray();

        model.UpcomingInterviews = await dbContext.Interviews.AsNoTracking()
            .Where(interview => interview.UserId == userId && interview.ScheduledAt >= now
                && (interview.Status == InterviewStatus.Scheduled || interview.Status == InterviewStatus.Rescheduled))
            .OrderBy(interview => interview.ScheduledAt)
            .Take(5)
            .Select(interview => new DashboardInterviewItemViewModel(
                interview.Id, interview.JobApplication!.CompanyName, interview.JobApplication.JobTitle,
                interview.InterviewType, interview.ScheduledAt))
            .ToListAsync();

        model.PendingReminders = await dbContext.Reminders.AsNoTracking()
            .Where(reminder => reminder.UserId == userId && !reminder.IsCompleted)
            .OrderBy(reminder => reminder.DueAt)
            .Take(5)
            .Select(reminder => new DashboardReminderItemViewModel(
                reminder.Id, reminder.Title,
                reminder.JobApplication != null ? reminder.JobApplication.CompanyName : null,
                reminder.DueAt, reminder.DueAt < now))
            .ToListAsync();

        model.UpcomingDeadlines = await dbContext.JobApplications.AsNoTracking()
            .Where(application => application.UserId == userId && application.Deadline != null
                && application.Deadline >= today)
            .OrderBy(application => application.Deadline)
            .Take(5)
            .Select(application => new DashboardDeadlineItemViewModel(
                application.Id, application.CompanyName, application.JobTitle, application.Deadline!.Value))
            .ToListAsync();

        var todayInterviewRows = await dbContext.Interviews.AsNoTracking()
            .Where(interview => interview.UserId == userId && interview.ScheduledAt >= todayStart
                && interview.ScheduledAt < tomorrowStart
                && interview.Status != InterviewStatus.Cancelled)
            .Select(interview => new DashboardInterviewItemViewModel(
                interview.Id, interview.JobApplication!.CompanyName, interview.JobApplication.JobTitle,
                interview.InterviewType, interview.ScheduledAt))
            .ToListAsync();
        var todayInterviews = todayInterviewRows.Select(interview => new DashboardActionItemViewModel(
            "Interview", interview.InterviewType.GetDisplayName(),
            interview.JobTitle + " at " + interview.CompanyName,
            interview.ScheduledAt, "Interviews", "Details", interview.Id)).ToList();

        var todayReminders = await dbContext.Reminders.AsNoTracking()
            .Where(reminder => reminder.UserId == userId && !reminder.IsCompleted
                && reminder.DueAt >= todayStart && reminder.DueAt < tomorrowStart)
            .Select(reminder => new DashboardActionItemViewModel(
                "Reminder", reminder.Title,
                reminder.JobApplication != null
                    ? reminder.JobApplication.JobTitle + " at " + reminder.JobApplication.CompanyName
                    : "Standalone reminder",
                reminder.DueAt, "Reminders", "Index", reminder.Id))
            .ToListAsync();

        var deadlineSortAt = todayStart.AddHours(23).AddMinutes(59);
        var todayDeadlines = await dbContext.JobApplications.AsNoTracking()
            .Where(application => application.UserId == userId && application.Deadline == today)
            .Select(application => new DashboardActionItemViewModel(
                "Deadline", "Application deadline",
                application.JobTitle + " at " + application.CompanyName,
                deadlineSortAt, "JobApplications", "Details", application.Id))
            .ToListAsync();

        model.TodayActions = todayInterviews.Concat(todayReminders).Concat(todayDeadlines)
            .OrderBy(item => item.SortAt).Take(8).ToArray();
        model.TodayActionCount = todayInterviews.Count + todayReminders.Count + todayDeadlines.Count;
        return View(model);
    }

    [Route("settings")] public IActionResult Settings() => Section("Settings", "Manage your ApplyWise account and preferences.");

    private IActionResult Section(string title, string description, string? actionLabel = null) =>
        View("Section", new SectionViewModel(title, description, actionLabel));
}

public sealed record SectionViewModel(string Title, string Description, string? ActionLabel = null);
