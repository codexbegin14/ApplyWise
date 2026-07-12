using ApplyWise.Web.Data;
using ApplyWise.Web.Models;
using ApplyWise.Web.ViewModels.Reminders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace ApplyWise.Web.Controllers;

[Authorize]
[Route("reminders")]
public class RemindersController(
    ApplicationDbContext dbContext,
    UserManager<IdentityUser> userManager) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index(string? filter)
    {
        var selectedFilter = filter is "completed" or "overdue" or "all" ? filter : "pending";
        var userId = GetUserId();
        var now = DateTimeOffset.UtcNow;
        var localNow = DateTimeOffset.Now;
        var todayStart = new DateTimeOffset(localNow.Date, localNow.Offset).ToUniversalTime();
        var tomorrowStart = todayStart.AddDays(1);
        var query = dbContext.Reminders.AsNoTracking().Where(reminder => reminder.UserId == userId);

        query = selectedFilter switch
        {
            "completed" => query.Where(reminder => reminder.IsCompleted)
                .OrderByDescending(reminder => reminder.CompletedAt),
            "overdue" => query.Where(reminder => !reminder.IsCompleted && reminder.DueAt < now)
                .OrderBy(reminder => reminder.DueAt),
            "all" => query.OrderBy(reminder => reminder.IsCompleted).ThenBy(reminder => reminder.DueAt),
            _ => query.Where(reminder => !reminder.IsCompleted).OrderBy(reminder => reminder.DueAt)
        };

        var reminders = await query.Select(reminder => new ReminderListItemViewModel(
            reminder.Id,
            reminder.Title,
            reminder.ReminderType,
            reminder.JobApplication != null ? reminder.JobApplication.CompanyName : null,
            reminder.JobApplication != null ? reminder.JobApplication.JobTitle : null,
            reminder.DueAt,
            reminder.IsCompleted,
            !reminder.IsCompleted && reminder.DueAt < now,
            !reminder.IsCompleted && reminder.DueAt >= todayStart && reminder.DueAt < tomorrowStart))
            .ToListAsync();

        return View(new ReminderIndexViewModel { Filter = selectedFilter, Reminders = reminders });
    }

    [HttpGet("create")]
    public async Task<IActionResult> Create(int? jobApplicationId)
    {
        var model = new ReminderCreateViewModel { DueAt = DateTimeOffset.Now.AddDays(7) };
        if (jobApplicationId.HasValue)
        {
            var userId = GetUserId();
            var application = await dbContext.JobApplications.AsNoTracking()
                .SingleOrDefaultAsync(item => item.Id == jobApplicationId.Value && item.UserId == userId);
            if (application is not null)
            {
                model.JobApplicationId = application.Id;
                if (application.Status == ApplicationStatus.Applied)
                {
                    model.Title = $"Follow up with {application.CompanyName}";
                    var dueDate = application.AppliedDate?.AddDays(7)
                        ?? DateOnly.FromDateTime(DateTime.Now.AddDays(7));
                    model.DueAt = AtLocalTime(dueDate, new TimeOnly(9, 0));
                }
            }
        }

        await PopulateApplicationsAsync(model);
        return View(model);
    }

    [HttpPost("create")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(ReminderCreateViewModel model)
    {
        await ValidateApplicationOwnershipAsync(model.JobApplicationId);
        if (!ModelState.IsValid)
        {
            await PopulateApplicationsAsync(model);
            return View(model);
        }

        var now = DateTimeOffset.UtcNow;
        var reminder = new Reminder { UserId = GetUserId(), CreatedAt = now, UpdatedAt = now };
        ApplyForm(reminder, model);
        dbContext.Reminders.Add(reminder);
        await dbContext.SaveChangesAsync();
        TempData["SuccessMessage"] = "Reminder added.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("{id:int}/edit")]
    public async Task<IActionResult> Edit(int id)
    {
        var reminder = await FindOwnedReminderAsync(id, readOnly: true);
        if (reminder is null) return NotFound();

        var model = new ReminderEditViewModel
        {
            Id = reminder.Id,
            Title = reminder.Title,
            ReminderType = reminder.ReminderType,
            JobApplicationId = reminder.JobApplicationId,
            DueAt = reminder.DueAt,
            Notes = reminder.Notes
        };
        await PopulateApplicationsAsync(model);
        return View(model);
    }

    [HttpPost("{id:int}/edit")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, ReminderEditViewModel model)
    {
        if (id != model.Id) return BadRequest();
        var reminder = await FindOwnedReminderAsync(id);
        if (reminder is null) return NotFound();

        await ValidateApplicationOwnershipAsync(model.JobApplicationId);
        if (!ModelState.IsValid)
        {
            await PopulateApplicationsAsync(model);
            return View(model);
        }

        ApplyForm(reminder, model);
        reminder.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync();
        TempData["SuccessMessage"] = "Reminder updated.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("{id:int}/complete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Complete(int id)
    {
        var reminder = await FindOwnedReminderAsync(id);
        if (reminder is null) return NotFound();

        if (!reminder.IsCompleted)
        {
            var now = DateTimeOffset.UtcNow;
            reminder.IsCompleted = true;
            reminder.CompletedAt = now;
            reminder.UpdatedAt = now;
            await dbContext.SaveChangesAsync();
        }

        TempData["SuccessMessage"] = "Reminder completed.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("{id:int}/delete")]
    public async Task<IActionResult> Delete(int id)
    {
        var userId = GetUserId();
        var reminder = await dbContext.Reminders.AsNoTracking()
            .Include(item => item.JobApplication)
            .SingleOrDefaultAsync(item => item.Id == id && item.UserId == userId);
        return reminder is null ? NotFound() : View(new ReminderListItemViewModel(
            reminder.Id, reminder.Title, reminder.ReminderType,
            reminder.JobApplication?.CompanyName, reminder.JobApplication?.JobTitle,
            reminder.DueAt, reminder.IsCompleted,
            !reminder.IsCompleted && reminder.DueAt < DateTimeOffset.UtcNow, false));
    }

    [HttpPost("{id:int}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var reminder = await FindOwnedReminderAsync(id);
        if (reminder is null) return NotFound();
        dbContext.Reminders.Remove(reminder);
        await dbContext.SaveChangesAsync();
        TempData["SuccessMessage"] = "Reminder deleted.";
        return RedirectToAction(nameof(Index));
    }

    private string GetUserId() => userManager.GetUserId(User)
        ?? throw new InvalidOperationException("The current user does not have an identifier.");

    private async Task ValidateApplicationOwnershipAsync(int? jobApplicationId)
    {
        if (!jobApplicationId.HasValue) return;
        var userId = GetUserId();
        if (!await dbContext.JobApplications.AnyAsync(application =>
                application.Id == jobApplicationId.Value && application.UserId == userId))
        {
            ModelState.AddModelError(nameof(ReminderFormViewModel.JobApplicationId),
                "Select a job application from your own tracker or leave it standalone.");
        }
    }

    private async Task PopulateApplicationsAsync(ReminderFormViewModel model)
    {
        var userId = GetUserId();
        model.AvailableApplications = await dbContext.JobApplications.AsNoTracking()
            .Where(application => application.UserId == userId)
            .OrderByDescending(application => application.CreatedAt)
            .Select(application => new SelectListItem(
                application.JobTitle + " at " + application.CompanyName,
                application.Id.ToString()))
            .ToListAsync();
    }

    private Task<Reminder?> FindOwnedReminderAsync(int id, bool readOnly = false)
    {
        IQueryable<Reminder> query = dbContext.Reminders;
        if (readOnly) query = query.AsNoTracking();
        var userId = GetUserId();
        return query.SingleOrDefaultAsync(reminder => reminder.Id == id && reminder.UserId == userId);
    }

    private static void ApplyForm(Reminder reminder, ReminderFormViewModel model)
    {
        reminder.Title = model.Title.Trim();
        reminder.ReminderType = model.ReminderType;
        reminder.JobApplicationId = model.JobApplicationId;
        reminder.DueAt = model.DueAt!.Value;
        reminder.Notes = string.IsNullOrWhiteSpace(model.Notes) ? null : model.Notes.Trim();
    }

    private static DateTimeOffset AtLocalTime(DateOnly date, TimeOnly time)
    {
        var localDateTime = date.ToDateTime(time, DateTimeKind.Unspecified);
        return new DateTimeOffset(localDateTime, TimeZoneInfo.Local.GetUtcOffset(localDateTime));
    }
}
