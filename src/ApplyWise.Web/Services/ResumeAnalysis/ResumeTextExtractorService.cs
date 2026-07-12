using System.Text;
using UglyToad.PdfPig;

namespace ApplyWise.Web.Services.ResumeAnalysis;

public sealed class ResumeTextExtractorService : IResumeTextExtractorService
{
    private const int MaxPages = 50;
    private const int MaxExtractedCharacters = 250_000;
    private static readonly SemaphoreSlim ExtractionSlots = new(initialCount: 2, maxCount: 2);

    public async Task<string?> ExtractTextAsync(string filePath, CancellationToken cancellationToken = default)
    {
        await ExtractionSlots.WaitAsync(cancellationToken);
        try
        {
            return await Task.Run(() => ExtractText(filePath, cancellationToken), cancellationToken);
        }
        finally
        {
            ExtractionSlots.Release();
        }
    }

    private static string? ExtractText(string filePath, CancellationToken cancellationToken)
    {
        try
        {
            using var document = PdfDocument.Open(filePath);
            if (document.NumberOfPages is <= 0 or > MaxPages)
            {
                return null;
            }

            var text = new StringBuilder();
            foreach (var page in document.GetPages())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!string.IsNullOrWhiteSpace(page.Text))
                {
                    var remaining = MaxExtractedCharacters - text.Length;
                    if (remaining <= 0)
                    {
                        return null;
                    }

                    var pageText = page.Text;
                    if (pageText.Length > remaining)
                    {
                        return null;
                    }

                    text.AppendLine(pageText);
                }
            }

            var extracted = text.ToString().Trim();
            return string.IsNullOrWhiteSpace(extracted) ? null : extracted;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }
}
