using ApplyWise.Web.Data;
using ApplyWise.Web.Models;
using ApplyWise.Web.ViewModels.Resumes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ApplyWise.Web.Controllers;

[Authorize]
[Route("resumes")]
public class ResumesController(
    ApplicationDbContext dbContext,
    UserManager<IdentityUser> userManager,
    IWebHostEnvironment environment) : Controller
{
    private const long MaxFileSize = 5 * 1024 * 1024;
    private static readonly byte[] PdfSignature = "%PDF-"u8.ToArray();

    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var userId = GetUserId();
        var resumes = await dbContext.Resumes
            .AsNoTracking()
            .Where(resume => resume.UserId == userId)
            .OrderByDescending(resume => resume.IsDefault)
            .ThenByDescending(resume => resume.UploadedAt)
            .Select(resume => new ResumeListItemViewModel(
                resume.Id, resume.VersionName, resume.OriginalFileName, resume.FileSize,
                resume.IsDefault, resume.UploadedAt, resume.Notes))
            .ToListAsync();

        return View(resumes);
    }

    [HttpGet("upload")]
    public IActionResult Create() => View(new ResumeUploadViewModel());

    [HttpPost("upload")]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(MaxFileSize + 64 * 1024)]
    public async Task<IActionResult> Create(ResumeUploadViewModel model)
    {
        await ValidateFileAsync(model.ResumeFile);
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var userId = GetUserId();
        var originalFileName = SanitizeFileName(model.ResumeFile!.FileName);
        var storedFileName = $"{Guid.NewGuid():N}.pdf";
        var relativePath = Path.Combine("App_Data", "Uploads", "Resumes", userId, storedFileName);
        var absolutePath = ResolvePrivatePath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);

        await using (var output = System.IO.File.Create(absolutePath))
        {
            await model.ResumeFile.CopyToAsync(output);
        }

        var now = DateTimeOffset.UtcNow;
        var resume = new Resume
        {
            UserId = userId,
            VersionName = model.VersionName.Trim(),
            OriginalFileName = originalFileName,
            StoredFileName = storedFileName,
            FilePath = relativePath,
            ContentType = "application/pdf",
            FileSize = model.ResumeFile.Length,
            Notes = string.IsNullOrWhiteSpace(model.Notes) ? null : model.Notes.Trim(),
            IsDefault = model.IsDefault,
            UploadedAt = now,
            UpdatedAt = now
        };

        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        try
        {
            if (resume.IsDefault)
            {
                await dbContext.Resumes
                    .Where(item => item.UserId == userId && item.IsDefault)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.IsDefault, false));
            }

            dbContext.Resumes.Add(resume);
            await dbContext.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            System.IO.File.Delete(absolutePath);
            throw;
        }

        TempData["SuccessMessage"] = $"{resume.VersionName} was uploaded.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> Details(int id)
    {
        var resume = await FindOwnedResumeAsync(id, true);
        return resume is null ? NotFound() : View(ToDetailsViewModel(resume));
    }

    [HttpGet("{id:int}/download")]
    public async Task<IActionResult> Download(int id)
    {
        var resume = await FindOwnedResumeAsync(id, true);
        if (resume is null)
        {
            return NotFound();
        }

        var absolutePath = ResolvePrivatePath(resume.FilePath);
        if (!System.IO.File.Exists(absolutePath))
        {
            return NotFound();
        }

        return PhysicalFile(absolutePath, resume.ContentType, resume.OriginalFileName, enableRangeProcessing: true);
    }

    [HttpPost("{id:int}/default")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetDefault(int id)
    {
        var userId = GetUserId();
        var resume = await dbContext.Resumes.SingleOrDefaultAsync(item => item.Id == id && item.UserId == userId);
        if (resume is null)
        {
            return NotFound();
        }

        if (resume.IsDefault)
        {
            TempData["SuccessMessage"] = $"{resume.VersionName} is already your default resume.";
            return RedirectToAction(nameof(Index));
        }

        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        await dbContext.Resumes
            .Where(item => item.UserId == userId && item.IsDefault)
            .ExecuteUpdateAsync(setters => setters.SetProperty(item => item.IsDefault, false));

        resume.IsDefault = true;
        resume.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync();
        await transaction.CommitAsync();

        TempData["SuccessMessage"] = $"{resume.VersionName} is now your default resume.";
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("{id:int}/delete")]
    public async Task<IActionResult> Delete(int id)
    {
        var resume = await FindOwnedResumeAsync(id, true);
        return resume is null ? NotFound() : View(ToDetailsViewModel(resume));
    }

    [HttpPost("{id:int}/delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var resume = await FindOwnedResumeAsync(id);
        if (resume is null)
        {
            return NotFound();
        }

        var now = DateTimeOffset.UtcNow;
        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        await dbContext.JobApplications
            .Where(application => application.UserId == resume.UserId && application.ResumeId == resume.Id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(application => application.ResumeId, (int?)null)
                .SetProperty(application => application.UpdatedAt, now));

        dbContext.Resumes.Remove(resume);
        await dbContext.SaveChangesAsync();
        await transaction.CommitAsync();

        var absolutePath = ResolvePrivatePath(resume.FilePath);
        if (System.IO.File.Exists(absolutePath))
        {
            System.IO.File.Delete(absolutePath);
        }

        TempData["SuccessMessage"] = $"{resume.VersionName} was deleted.";
        return RedirectToAction(nameof(Index));
    }

    private string GetUserId() => userManager.GetUserId(User)
        ?? throw new InvalidOperationException("The current user does not have an identifier.");

    private Task<Resume?> FindOwnedResumeAsync(int id, bool readOnly = false)
    {
        IQueryable<Resume> query = dbContext.Resumes;
        if (readOnly)
        {
            query = query.AsNoTracking();
        }

        var userId = GetUserId();
        return query.SingleOrDefaultAsync(resume => resume.Id == id && resume.UserId == userId);
    }

    private async Task ValidateFileAsync(IFormFile? file)
    {
        if (file is null)
        {
            return;
        }

        if (file.Length == 0)
        {
            ModelState.AddModelError(nameof(ResumeUploadViewModel.ResumeFile), "The selected file is empty.");
        }
        else if (file.Length > MaxFileSize)
        {
            ModelState.AddModelError(nameof(ResumeUploadViewModel.ResumeFile), "The PDF must be 5 MB or smaller.");
        }

        if (!string.Equals(Path.GetExtension(file.FileName), ".pdf", StringComparison.OrdinalIgnoreCase))
        {
            ModelState.AddModelError(nameof(ResumeUploadViewModel.ResumeFile), "Only PDF files are supported.");
        }

        if (!string.Equals(file.ContentType, "application/pdf", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(file.ContentType, "application/x-pdf", StringComparison.OrdinalIgnoreCase))
        {
            ModelState.AddModelError(nameof(ResumeUploadViewModel.ResumeFile), "The selected file is not recognized as a PDF.");
        }

        if (file.Length > 0 && file.Length <= MaxFileSize)
        {
            await using var stream = file.OpenReadStream();
            var header = new byte[PdfSignature.Length];
            var bytesRead = await stream.ReadAsync(header);
            if (bytesRead != PdfSignature.Length || !header.SequenceEqual(PdfSignature))
            {
                ModelState.AddModelError(nameof(ResumeUploadViewModel.ResumeFile), "The selected file does not contain a valid PDF header.");
            }
        }
    }

    private string ResolvePrivatePath(string relativePath)
    {
        var contentRoot = Path.GetFullPath(environment.ContentRootPath);
        var absolutePath = Path.GetFullPath(Path.Combine(contentRoot, relativePath));
        var storageRoot = Path.GetFullPath(Path.Combine(contentRoot, "App_Data", "Uploads", "Resumes")) + Path.DirectorySeparatorChar;

        if (!absolutePath.StartsWith(storageRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The resume path is outside the private storage directory.");
        }

        return absolutePath;
    }

    private static string SanitizeFileName(string fileName)
    {
        var baseName = Path.GetFileName(fileName);
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var safeName = new string(baseName.Where(character =>
            !invalidCharacters.Contains(character) && !char.IsControl(character)).ToArray()).Trim();

        if (string.IsNullOrWhiteSpace(safeName))
        {
            safeName = "resume.pdf";
        }

        return safeName.Length <= 255 ? safeName : safeName[..251] + ".pdf";
    }

    private static ResumeDetailsViewModel ToDetailsViewModel(Resume resume) =>
        new(resume.Id, resume.VersionName, resume.OriginalFileName, resume.FileSize,
            resume.IsDefault, resume.UploadedAt, resume.UpdatedAt, resume.Notes);
}
