using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using UglyToad.PdfPig.Exceptions;

namespace ApplyWise.Web.Services.ResumeAnalysis;

public static class PdfTextInspector
{
    public const int MaxPages = 50;
    public const int MaxExtractedCharacters = 250_000;

    public static PdfTextExtractionResult Inspect(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var parsingOptions = new ParsingOptions
            {
                UseLenientParsing = true,
                SkipMissingFonts = true,
                UseActualText = true,
                MaxStackDepth = 64
            };
            using var document = PdfDocument.Open(filePath, parsingOptions);
            if (document.IsEncrypted)
            {
                return new PdfTextExtractionResult(PdfTextExtractionStatus.Encrypted);
            }

            if (document.NumberOfPages is <= 0 or > MaxPages)
            {
                return new PdfTextExtractionResult(PdfTextExtractionStatus.PageLimitExceeded);
            }

            var text = new StringBuilder();
            for (var pageNumber = 1; pageNumber <= document.NumberOfPages; pageNumber++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var page = document.GetPage(pageNumber);
                var pageText = ContentOrderTextExtractor.GetText(page);
                if (string.IsNullOrWhiteSpace(pageText)) continue;

                pageText = Sanitize(pageText);
                var remaining = MaxExtractedCharacters - text.Length;
                if (remaining <= 0 || pageText.Length > remaining)
                {
                    return new PdfTextExtractionResult(PdfTextExtractionStatus.TextLimitExceeded);
                }

                text.AppendLine(pageText);
            }

            var extracted = text.ToString().Trim();
            return string.IsNullOrWhiteSpace(extracted)
                ? new PdfTextExtractionResult(PdfTextExtractionStatus.NoText)
                : new PdfTextExtractionResult(PdfTextExtractionStatus.Success, extracted);
        }
        catch (PdfDocumentEncryptedException)
        {
            return new PdfTextExtractionResult(PdfTextExtractionStatus.Encrypted);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return new PdfTextExtractionResult(PdfTextExtractionStatus.Invalid);
        }
    }

    private static string Sanitize(string value)
    {
        var characters = value.Select(character =>
            !char.IsControl(character) || character is '\r' or '\n' or '\t' ? character : ' ');
        return new string(characters.ToArray()).Trim();
    }
}
