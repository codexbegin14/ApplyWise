using System.Text;
using UglyToad.PdfPig;

namespace ApplyWise.Web.Services.ResumeAnalysis;

public sealed class ResumeTextExtractorService : IResumeTextExtractorService
{
    public Task<string?> ExtractTextAsync(string filePath, CancellationToken cancellationToken = default) =>
        Task.Run(() => ExtractText(filePath, cancellationToken), cancellationToken);

    private static string? ExtractText(string filePath, CancellationToken cancellationToken)
    {
        try
        {
            using var document = PdfDocument.Open(filePath);
            var text = new StringBuilder();
            foreach (var page in document.GetPages())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!string.IsNullOrWhiteSpace(page.Text))
                {
                    text.AppendLine(page.Text);
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
