using System.Data;
using ApplyWise.Web.Data;
using ApplyWise.Web.Models;
using ApplyWise.Web.Services.ResumeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ApplyWise.Web.Services.ResumeStorage;

public sealed class ResumeIngestionService(
    ApplicationDbContext dbContext,
    IResumeStorageService resumeStorage,
    IResumeTextExtractorService textExtractor,
    IOptions<ResumeStorageOptions> storageOptions,
    ILogger<ResumeIngestionService> logger) : IResumeIngestionService
{
    private static readonly byte[] PdfSignature = "%PDF-"u8.ToArray();
    private static readonly byte[] Utf8Bom = [0xEF, 0xBB, 0xBF];

    public async Task<ResumeIngestionResult> IngestAsync(
        ResumeIngestionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(request.UserId);

        var validationErrors = await ValidateFileAsync(request.File, cancellationToken);
        if (validationErrors.Count > 0)
        {
            return ResumeIngestionResult.Failed(validationErrors);
        }

        var file = request.File!;
        var limits = storageOptions.Value;
        var usage = await GetUsageAsync(request.UserId, cancellationToken);
        if (ExceedsStorageLimit(usage.Count, usage.Bytes, file.Length, limits))
        {
            return ResumeIngestionResult.Failed(
            [
                $"Your resume library is limited to {limits.MaxFilesPerUser} files and {limits.MaxBytesPerUser / (1024 * 1024)} MB."
            ]);
        }

        var originalFileName = SanitizeFileName(file.FileName);
        var storedFileName = $"{Guid.NewGuid():N}.pdf";
        var relativePath = resumeStorage.CreateRelativePath(request.UserId, storedFileName);
        var absolutePath = resumeStorage.ResolvePath(relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);

        PdfTextExtractionResult inspection;
        try
        {
            await using (var output = File.Create(absolutePath))
            {
                await file.CopyToAsync(output, cancellationToken);
            }

            inspection = await textExtractor.InspectAsync(absolutePath, cancellationToken);
        }
        catch
        {
            TryDelete(absolutePath);
            throw;
        }

        if (!inspection.IsValidDocument)
        {
            TryDelete(absolutePath);
            return ResumeIngestionResult.Failed([GetInspectionError(inspection.Status)], inspection);
        }

        if (request.RequireSelectableText
            && (inspection.Status != PdfTextExtractionStatus.Success
                || string.IsNullOrWhiteSpace(inspection.Text)))
        {
            TryDelete(absolutePath);
            return ResumeIngestionResult.Failed(
                ["No selectable text was found. Upload a text-based PDF exported directly from your editor."],
                inspection);
        }

        var now = DateTimeOffset.UtcNow;
        var resume = new Resume
        {
            UserId = request.UserId,
            VersionName = request.VersionName.Trim(),
            OriginalFileName = originalFileName,
            StoredFileName = storedFileName,
            FilePath = relativePath,
            ContentType = "application/pdf",
            FileSize = file.Length,
            Notes = string.IsNullOrWhiteSpace(request.Notes) ? null : request.Notes.Trim(),
            IsDefault = request.IsDefault,
            UploadedAt = now,
            UpdatedAt = now,
            ExtractedText = inspection.Text
        };

        await using var transaction = await dbContext.Database.BeginTransactionAsync(
            IsolationLevel.Serializable,
            cancellationToken);
        try
        {
            var currentUsage = await GetUsageAsync(request.UserId, cancellationToken);
            if (ExceedsStorageLimit(currentUsage.Count, currentUsage.Bytes, resume.FileSize, limits))
            {
                await transaction.RollbackAsync(cancellationToken);
                TryDelete(absolutePath);
                return ResumeIngestionResult.Failed(
                    ["Your resume library reached its storage limit while this upload was being prepared."],
                    inspection);
            }

            if (resume.IsDefault)
            {
                await dbContext.Resumes
                    .Where(item => item.UserId == request.UserId && item.IsDefault)
                    .ExecuteUpdateAsync(
                        setters => setters.SetProperty(item => item.IsDefault, false),
                        cancellationToken);
            }

            dbContext.Resumes.Add(resume);
            await dbContext.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            TryDelete(absolutePath);
            throw;
        }

        return new ResumeIngestionResult(resume, inspection, []);
    }

    private async Task<(int Count, long Bytes)> GetUsageAsync(
        string userId,
        CancellationToken cancellationToken)
    {
        var usage = await dbContext.Resumes
            .Where(resume => resume.UserId == userId)
            .GroupBy(_ => 1)
            .Select(group => new { Count = group.Count(), Bytes = group.Sum(resume => resume.FileSize) })
            .SingleOrDefaultAsync(cancellationToken);
        return usage is null ? (0, 0L) : (usage.Count, usage.Bytes);
    }

    private static bool ExceedsStorageLimit(
        int fileCount,
        long storedBytes,
        long incomingBytes,
        ResumeStorageOptions limits) =>
        fileCount >= limits.MaxFilesPerUser
        || storedBytes > limits.MaxBytesPerUser - incomingBytes;

    private static async Task<IReadOnlyList<string>> ValidateFileAsync(
        IFormFile? file,
        CancellationToken cancellationToken)
    {
        if (file is null)
        {
            return ["Choose a PDF resume to upload."];
        }

        var errors = new List<string>();
        if (file.Length == 0)
        {
            errors.Add("The selected file is empty.");
        }
        else if (file.Length > ResumeIngestionLimits.MaxFileSizeBytes)
        {
            errors.Add("The PDF must be 5 MB or smaller.");
        }

        if (!string.Equals(Path.GetExtension(file.FileName), ".pdf", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("Only PDF files are supported.");
        }

        if (file.Length > 0 && file.Length <= ResumeIngestionLimits.MaxFileSizeBytes)
        {
            await using var stream = file.OpenReadStream();
            if (!await HasPdfSignatureAsync(stream, cancellationToken))
            {
                errors.Add("The selected file does not contain a valid PDF header.");
            }
        }

        return errors;
    }

    private static async Task<bool> HasPdfSignatureAsync(
        Stream stream,
        CancellationToken cancellationToken)
    {
        var header = new byte[PdfSignature.Length + 8];
        var bytesRead = await stream.ReadAsync(header, cancellationToken);
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

    private void TryDelete(string absolutePath)
    {
        try
        {
            if (File.Exists(absolutePath))
            {
                File.Delete(absolutePath);
            }
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Could not remove an uncommitted resume upload.");
        }
    }
}
