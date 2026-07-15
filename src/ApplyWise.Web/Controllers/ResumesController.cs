using ApplyWise.Web.Data;
using ApplyWise.Web.Models;
using ApplyWise.Web.Services.ResumeAnalysis;
using ApplyWise.Web.Services.ResumeStorage;
using ApplyWise.Web.ViewModels.Resumes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using System.Data;

namespace ApplyWise.Web.Controllers;

[Authorize]
[Route("resumes")]
public class ResumesController(
    ApplicationDbContext dbContext,
    UserManager<IdentityUser> userManager,
    IResumeStorageService resumeStorage,
    IResumeTextExtractorService textExtractor,
    IOptions<ResumeStorageOptions> storageOptions) : Controller
{
    private const long MaxFileSize = 5 * 1024 * 1024;
    private static readonly byte[] PdfSignature = "%PDF-"u8.ToArray();
    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

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
    [EnableRateLimiting("uploads")]
    [RequestSizeLimit(MaxFileSize + 64 * 1024)]
    public async Task<IActionResult> Create(ResumeUploadViewModel model)
    {
        await ValidateFileAsync(model.ResumeFile);
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var userId = GetUserId();
        var limits = storageOptions.Value;
        var usage = await dbContext.Resumes.Where(resume => resume.UserId == userId)
            .GroupBy(_ => 1).Select(group => new { Count = group.Count(), Bytes = group.Sum(resume => resume.FileSize) })
            .SingleOrDefaultAsync() ?? new { Count = 0, Bytes = 0L };
        if (usage.Count >= limits.MaxFilesPerUser || usage.Bytes > limits.MaxBytesPerUser - model.ResumeFile!.Length)
        {
            ModelState.AddModelError(nameof(model.ResumeFile), $"Your resume library is limited to {limits.MaxFilesPerUser} files and {limits.MaxBytesPerUser / (1024 * 1024)} MB.");
            return View(model);
        }
        var originalFileName = SanitizeFileName(model.ResumeFile!.FileName);
        var storedFileName = $"{Guid.NewGuid():N}.pdf";
        var relativePath = resumeStorage.CreateRelativePath(userId, storedFileName);
        var absolutePath = resumeStorage.ResolvePath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);

        PdfTextExtractionResult inspection;
        try
        {
            await using (var output = System.IO.File.Create(absolutePath))
            {
                await model.ResumeFile.CopyToAsync(output);
            }
            inspection = await textExtractor.InspectAsync(absolutePath, HttpContext.RequestAborted);
        }
        catch
        {
            System.IO.File.Delete(absolutePath);
            throw;
        }

        if (!inspection.IsValidDocument)
        {
            System.IO.File.Delete(absolutePath);
            ModelState.AddModelError(nameof(model.ResumeFile), GetInspectionError(inspection.Status));
            return View(model);
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
            UpdatedAt = now,
            ExtractedText = inspection.Text
        };

        await using var transaction = await dbContext.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        try
        {
            var currentUsage = await dbContext.Resumes.Where(resume => resume.UserId == userId)
                .GroupBy(_ => 1).Select(group => new { Count = group.Count(), Bytes = group.Sum(resume => resume.FileSize) })
                .SingleOrDefaultAsync() ?? new { Count = 0, Bytes = 0L };
            if (currentUsage.Count >= limits.MaxFilesPerUser || currentUsage.Bytes > limits.MaxBytesPerUser - resume.FileSize)
            {
                await transaction.RollbackAsync();
                System.IO.File.Delete(absolutePath);
                ModelState.AddModelError(nameof(model.ResumeFile), "Your resume library reached its storage limit while this upload was being prepared.");
                return View(model);
            }
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
        if (inspection.Status == PdfTextExtractionStatus.NoText)
        {
            TempData["WarningMessage"] =
                "No selectable text was found. You can store and download this PDF, but resume analysis and matching require a text-based PDF.";
        }
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

        var absolutePath = resumeStorage.ResolvePath(resume.FilePath);
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
        await dbContext.ResumeAnalyses
            .Where(analysis => analysis.UserId == resume.UserId && analysis.ResumeId == resume.Id)
            .ExecuteDeleteAsync();
        await dbContext.JobApplications
            .Where(application => application.UserId == resume.UserId && application.ResumeId == resume.Id)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(application => application.ResumeId, (int?)null)
                .SetProperty(application => application.UpdatedAt, now));

        dbContext.Resumes.Remove(resume);
        await dbContext.SaveChangesAsync();
        await transaction.CommitAsync();

        var absolutePath = resumeStorage.ResolvePath(resume.FilePath);
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

        if (file.Length > 0 && file.Length <= MaxFileSize)
        {
            await using var stream = file.OpenReadStream();
            if (!await HasPdfSignatureAsync(stream))
            {
                ModelState.AddModelError(nameof(ResumeUploadViewModel.ResumeFile), "The selected file does not contain a valid PDF header.");
            }
        }
    }

    private static async Task<bool> HasPdfSignatureAsync(Stream stream)
    {
        var header = new byte[PdfSignature.Length + 8];
        var bytesRead = await stream.ReadAsync(header);
        var offset = header.AsSpan(0, bytesRead).StartsWith(Utf8Bom) ? Utf8Bom.Length : 0;
        while (offset < bytesRead && header[offset] is (byte)' ' or (byte)'\t' or (byte)'\r' or (byte)'\n')
        {
            offset++;
        }

        return bytesRead - offset >= PdfSignature.Length
               && header.AsSpan(offset, PdfSignature.Length).SequenceEqual(PdfSignature);
    }

    private static string GetInspectionError(PdfTextExtractionStatus status) => status switch
    {
        PdfTextExtractionStatus.Encrypted =>
            "Password-protected or encrypted PDFs are not supported. Save an unprotected copy and try again.",
        PdfTextExtractionStatus.PageLimitExceeded =>
            $"The PDF must contain between 1 and {PdfTextInspector.MaxPages} pages.",
        PdfTextExtractionStatus.TextLimitExceeded =>
            "The PDF contains too much embedded text to process safely.",
        PdfTextExtractionStatus.TimedOut =>
            "The PDF took too long to inspect. Try exporting a simpler PDF and upload it again.",
        PdfTextExtractionStatus.Unavailable =>
            "The PDF could not be inspected right now. Please try again.",
        _ => "The selected file is damaged or is not a valid PDF."
    };

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
