using ApplyWise.Web.Data;
using ApplyWise.Web.Models;
using ApplyWise.Web.Services.Gmail;
using ApplyWise.Web.ViewModels.ApplicationImports;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ApplyWise.Web.Controllers;

[Authorize]
[Route("application-imports")]
public sealed class ApplicationImportsController(
    ApplicationDbContext dbContext,
    UserManager<IdentityUser> userManager,
    IGmailImportService gmailImportService,
    IOptions<GoogleIntegrationOptions> googleOptions) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var userId = GetUserId();
        var connection = await dbContext.GmailConnections
            .AsNoTracking()
            .Where(item => item.UserId == userId)
            .Select(item => new GmailConnectionSummaryViewModel(
                item.EmailAddress,
                item.ConnectedAt,
                item.LastSuccessfulSyncAt,
                item.LastSyncStartedAt,
                item.LastErrorCode))
            .SingleOrDefaultAsync(HttpContext.RequestAborted);
        var pendingImports = await dbContext.ApplicationImports
            .AsNoTracking()
            .Where(item =>
                item.UserId == userId
                && item.Status == ApplicationImportStatus.PendingReview)
            .OrderByDescending(item => item.DetectedAt)
            .Take(100)
            .Select(item => new ApplicationImportListItemViewModel(
                item.Id,
                item.EmailSubject,
                item.CompanyName,
                item.JobTitle,
                item.Source,
                item.Direction,
                item.Confidence,
                item.AppliedDate,
                item.ResumeFileName,
                item.DetectedAt))
            .ToListAsync(HttpContext.RequestAborted);

        return View(new ApplicationImportIndexViewModel
        {
            GoogleIntegrationConfigured = googleOptions.Value.IsConfigured,
            GmailConnection = connection,
            PendingImports = pendingImports
        });
    }

    [HttpPost("sync")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("gmail-sync")]
    public async Task<IActionResult> Sync()
    {
        var result = await gmailImportService.SyncUserAsync(
            GetUserId(),
            HttpContext.RequestAborted);
        TempData[result.Succeeded ? "ImportSuccess" : "ImportError"] = result.Message;
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("{id:int}/review")]
    public async Task<IActionResult> Review(int id)
    {
        var item = await FindPendingImportAsync(id);
        if (item is null) return NotFound();
        return View(ToReviewModel(item));
    }

    [HttpPost("{id:int}/review")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Review(
        int id,
        ApplicationImportReviewViewModel model)
    {
        if (id != model.Id) return BadRequest();
        var item = await FindPendingImportAsync(id);
        if (item is null) return NotFound();
        if (!ModelState.IsValid)
        {
            CopyEvidence(item, model);
            return View(model);
        }

        var userId = GetUserId();
        var companyName = model.CompanyName.Trim();
        var jobTitle = model.JobTitle.Trim();
        var jobUrl = model.JobUrl?.Trim();
        var existingApplication = await dbContext.JobApplications
            .FirstOrDefaultAsync(
                application =>
                    application.UserId == userId
                    && application.CompanyName == companyName
                    && application.JobTitle == jobTitle
                    && application.AppliedDate == model.AppliedDate,
                HttpContext.RequestAborted);

        if (existingApplication is null)
        {
            var resumeId = await FindResumeIdAsync(
                userId,
                item.ResumeFileName,
                HttpContext.RequestAborted);
            var now = DateTimeOffset.UtcNow;
            existingApplication = new JobApplication
            {
                UserId = userId,
                ResumeId = resumeId,
                CompanyName = companyName,
                JobTitle = jobTitle,
                JobLocation = NullIfWhiteSpace(model.JobLocation),
                Source = model.Source,
                JobUrl = NullIfWhiteSpace(jobUrl),
                Status = ApplicationStatus.Applied,
                AppliedDate = model.AppliedDate,
                Notes =
                    "Imported from a connected Gmail account. Verify any details that were not present in the original confirmation.",
                CreatedAt = now,
                UpdatedAt = now
            };
            dbContext.JobApplications.Add(existingApplication);
            await dbContext.SaveChangesAsync(HttpContext.RequestAborted);
        }

        item.Status = ApplicationImportStatus.Accepted;
        item.CreatedApplicationId = existingApplication.Id;
        item.ReviewedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(HttpContext.RequestAborted);
        TempData["SuccessMessage"] =
            $"{existingApplication.JobTitle} at {existingApplication.CompanyName} was added from Gmail.";
        return RedirectToAction(
            "Details",
            "JobApplications",
            new { id = existingApplication.Id });
    }

    [HttpPost("{id:int}/dismiss")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Dismiss(int id)
    {
        var item = await FindPendingImportAsync(id);
        if (item is null) return NotFound();
        item.Status = ApplicationImportStatus.Dismissed;
        item.ReviewedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(HttpContext.RequestAborted);
        TempData["ImportSuccess"] = "The email suggestion was dismissed.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<ApplicationImport?> FindPendingImportAsync(int id)
    {
        var userId = GetUserId();
        return await dbContext.ApplicationImports.SingleOrDefaultAsync(
            item =>
                item.Id == id
                && item.UserId == userId
                && item.Status == ApplicationImportStatus.PendingReview,
            HttpContext.RequestAborted);
    }

    private static ApplicationImportReviewViewModel ToReviewModel(
        ApplicationImport item) =>
        new()
        {
            Id = item.Id,
            EmailSubject = item.EmailSubject,
            SenderDomain = item.SenderDomain,
            Direction = item.Direction,
            Confidence = item.Confidence,
            ResumeFileName = item.ResumeFileName,
            CompanyName = item.CompanyName,
            JobTitle = item.JobTitle,
            JobLocation = item.JobLocation,
            Source = item.Source,
            JobUrl = item.JobUrl,
            AppliedDate = item.AppliedDate
        };

    private static void CopyEvidence(
        ApplicationImport item,
        ApplicationImportReviewViewModel model)
    {
        model.EmailSubject = item.EmailSubject;
        model.SenderDomain = item.SenderDomain;
        model.Direction = item.Direction;
        model.Confidence = item.Confidence;
        model.ResumeFileName = item.ResumeFileName;
    }

    private async Task<int?> FindResumeIdAsync(
        string userId,
        string? resumeFileName,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(resumeFileName))
        {
            var matchedResume = await dbContext.Resumes
                .Where(resume =>
                    resume.UserId == userId
                    && resume.OriginalFileName == resumeFileName)
                .OrderByDescending(resume => resume.UploadedAt)
                .Select(resume => (int?)resume.Id)
                .FirstOrDefaultAsync(cancellationToken);
            if (matchedResume.HasValue) return matchedResume;
        }

        return await dbContext.Resumes
            .Where(resume => resume.UserId == userId && resume.IsDefault)
            .Select(resume => (int?)resume.Id)
            .SingleOrDefaultAsync(cancellationToken);
    }

    private string GetUserId() =>
        userManager.GetUserId(User)
        ?? throw new InvalidOperationException(
            "The current user does not have an identifier.");

    private static string? NullIfWhiteSpace(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
