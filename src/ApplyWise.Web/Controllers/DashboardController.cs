using ApplyWise.Web.Data;
using ApplyWise.Web.Models;
using ApplyWise.Web.Services.Analytics;
using ApplyWise.Web.Services.AccountSecurity;
using ApplyWise.Web.Services.ResumeStorage;
using ApplyWise.Web.ViewModels.Dashboard;
using ApplyWise.Web.ViewModels.Settings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;

namespace ApplyWise.Web.Controllers;

[Authorize]
public class DashboardController(
    ApplicationDbContext dbContext,
    UserManager<IdentityUser> userManager,
    IAnalyticsService analyticsService,
    IAccountSecurityCodeService securityCodes,
    IResumeStorageService resumeStorage,
    SignInManager<IdentityUser> signInManager,
    ILogger<DashboardController> logger) : Controller
{
    public async Task<IActionResult> Index(ApplicationStatus? tab)
    {
        var userId = userManager.GetUserId(User)
            ?? throw new InvalidOperationException("The current user does not have an identifier.");
        var currentUser = await userManager.GetUserAsync(User)
            ?? throw new InvalidOperationException("The current user could not be loaded.");
        var displayName = await dbContext.CareerProfiles.AsNoTracking().Where(profile => profile.UserId == userId).Select(profile => profile.FullName).SingleOrDefaultAsync();
        if (string.IsNullOrWhiteSpace(displayName))
        {
            displayName = (await userManager.GetClaimsAsync(currentUser))
                .FirstOrDefault(claim => claim.Type == "display_name")?.Value;
        }
        displayName = string.IsNullOrWhiteSpace(displayName)
            ? currentUser.UserName?.Split('@')[0] ?? "there"
            : displayName.Trim();
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
            DisplayName = displayName,
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
        model.PipelineApplications = await dbContext.JobApplications.AsNoTracking()
            .Where(application => application.UserId == userId)
            .OrderByDescending(application => application.UpdatedAt)
            .Select(application => new RecentApplicationItem(
                application.Id, application.CompanyName, application.JobTitle, application.Status, application.CreatedAt))
            .ToListAsync();
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
        ViewData["SelectedPipelineStatus"] = tab is { } selectedStatus && Enum.IsDefined(selectedStatus)
            ? selectedStatus
            : ApplicationStatus.Applied;
        return View(model);
    }

    [HttpGet("settings")]
    public async Task<IActionResult> Settings() => View(await BuildSettingsModelAsync());

    [HttpPost("settings/security-code/{securityAction}"), ValidateAntiForgeryToken]
    [EnableRateLimiting("account-security")]
    public async Task<IActionResult> SendSecurityCode(string securityAction)
    {
        if (!TryParseAction(securityAction, out var accountSecurityAction)) return NotFound();
        var user = await userManager.GetUserAsync(User);
        if (user is null || string.IsNullOrWhiteSpace(user.Email)) return Challenge();

        var issued = await securityCodes.IssueAsync(user.Id, user.Email, accountSecurityAction, HttpContext.RequestAborted);
        if (issued.Succeeded)
        {
            TempData["SettingsSuccess"] = issued.Message;
        }
        else
        {
            TempData["SettingsError"] = issued.Message;
        }
        TempData["SettingsOpenSection"] = securityAction;
        return RedirectToAction(nameof(Settings));
    }

    [HttpPost("settings/change-password"), ValidateAntiForgeryToken]
    [EnableRateLimiting("account-security")]
    public async Task<IActionResult> ChangePassword([Bind(Prefix = "ChangePassword")] ChangePasswordInput input)
    {
        if (!ModelState.IsValid) return await SettingsWithErrorsAsync("password");
        var user = await userManager.GetUserAsync(User);
        if (user is null) return Challenge();

        var verified = await securityCodes.VerifyAsync(user.Id, AccountSecurityAction.ChangePassword, input.Code, HttpContext.RequestAborted);
        if (!verified.Succeeded)
        {
            ModelState.AddModelError("ChangePassword.Code", verified.Message);
            return await SettingsWithErrorsAsync("password");
        }

        var result = await userManager.ChangePasswordAsync(user, input.CurrentPassword, input.NewPassword);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors) ModelState.AddModelError("ChangePassword.CurrentPassword", error.Description);
            return await SettingsWithErrorsAsync("password");
        }

        await securityCodes.ConsumeAsync(verified.CodeId!.Value, HttpContext.RequestAborted);
        await signInManager.RefreshSignInAsync(user);
        TempData["SettingsSuccess"] = "Your password was changed successfully.";
        return RedirectToAction(nameof(Settings));
    }

    [HttpPost("settings/delete-account"), ValidateAntiForgeryToken]
    [EnableRateLimiting("account-security")]
    public async Task<IActionResult> DeleteAccount([Bind(Prefix = "DeleteAccount")] DeleteAccountInput input)
    {
        if (!ModelState.IsValid) return await SettingsWithErrorsAsync("delete");
        var user = await userManager.GetUserAsync(User);
        if (user is null) return Challenge();

        var verified = await securityCodes.VerifyAsync(user.Id, AccountSecurityAction.DeleteAccount, input.Code, HttpContext.RequestAborted);
        if (!verified.Succeeded)
        {
            ModelState.AddModelError("DeleteAccount.Code", verified.Message);
            return await SettingsWithErrorsAsync("delete");
        }

        var resumePaths = await dbContext.Resumes.AsNoTracking().Where(resume => resume.UserId == user.Id)
            .Select(resume => resume.FilePath).ToListAsync(HttpContext.RequestAborted);
        var result = await userManager.DeleteAsync(user);
        if (!result.Succeeded)
        {
            foreach (var error in result.Errors) ModelState.AddModelError(string.Empty, error.Description);
            return await SettingsWithErrorsAsync("delete");
        }

        foreach (var path in resumePaths)
        {
            try
            {
                var filePath = resumeStorage.ResolvePath(path);
                if (System.IO.File.Exists(filePath)) System.IO.File.Delete(filePath);
            }
            catch (Exception exception)
            {
                logger.LogWarning(exception, "Could not remove a private resume file while deleting account {UserId}.", user.Id);
            }
        }

        await signInManager.SignOutAsync();
        TempData["StatusMessage"] = "Your ApplyWise account and private data were deleted.";
        return RedirectToPage("/Account/Login", new { area = "Identity" });
    }

    private IActionResult Section(string title, string description, string? actionLabel = null) =>
        View("Section", new SectionViewModel(title, description, actionLabel));

    private async Task<SettingsViewModel> BuildSettingsModelAsync()
    {
        var user = await userManager.GetUserAsync(User) ?? throw new InvalidOperationException("The current user could not be loaded.");
        if (TempData["SettingsOpenSection"] is string requestedSection)
        {
            ViewData["SettingsOpenSection"] = requestedSection;
        }
        return new SettingsViewModel
        {
            Email = user.Email ?? user.UserName ?? string.Empty
        };
    }

    private async Task<IActionResult> SettingsWithErrorsAsync(string section)
    {
        ViewData["SettingsOpenSection"] = section;
        return View("Settings", await BuildSettingsModelAsync());
    }

    private static bool TryParseAction(string action, out AccountSecurityAction securityAction)
    {
        if (string.Equals(action, "password", StringComparison.OrdinalIgnoreCase))
        {
            securityAction = AccountSecurityAction.ChangePassword;
            return true;
        }
        if (string.Equals(action, "delete", StringComparison.OrdinalIgnoreCase))
        {
            securityAction = AccountSecurityAction.DeleteAccount;
            return true;
        }
        securityAction = default;
        return false;
    }
}

public sealed record SectionViewModel(string Title, string Description, string? ActionLabel = null);
