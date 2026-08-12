using System.Text;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using UglyToad.PdfPig.DocumentLayoutAnalysis.WordExtractor;
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
            var pageTexts = new List<string>();
            var suspectedMultiColumn = false;
            var hasRotatedText = false;
            var verySmallLetters = 0;
            var totalLetters = 0;
            for (var pageNumber = 1; pageNumber <= document.NumberOfPages; pageNumber++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var page = document.GetPage(pageNumber);
                var pageText = ContentOrderTextExtractor.GetText(page);
                if (string.IsNullOrWhiteSpace(pageText)) continue;

                totalLetters += page.Letters.Count;
                verySmallLetters += page.Letters.Count(letter => letter.PointSize is > 0 and < 8);
                hasRotatedText |= page.Letters.Any(letter =>
                    letter.TextOrientation != UglyToad.PdfPig.Content.TextOrientation.Horizontal);
                suspectedMultiColumn |= HasLikelyColumns(page);

                pageText = Sanitize(pageText);
                pageTexts.Add(pageText);
                var remaining = MaxExtractedCharacters - text.Length;
                if (remaining <= 0 || pageText.Length > remaining)
                {
                    return new PdfTextExtractionResult(PdfTextExtractionStatus.TextLimitExceeded);
                }

                text.AppendLine(pageText);
            }

            var extracted = text.ToString().Trim();
            var diagnostics = new ResumeFileDiagnostics(
                "PDF",
                LayoutAssessed: true,
                SuspectedMultiColumn: suspectedMultiColumn,
                RepeatedHeaderOrFooter: HasRepeatedHeaderOrFooter(pageTexts, document.NumberOfPages),
                HasRotatedText: hasRotatedText,
                HasVerySmallText: totalLetters > 0 && verySmallLetters >= 20 && verySmallLetters >= totalLetters * .08,
                Notes: ["PDF layout checks are heuristic; confirm the extracted text reads in the intended order."]);
            return string.IsNullOrWhiteSpace(extracted)
                ? new PdfTextExtractionResult(PdfTextExtractionStatus.NoText, PageCount: document.NumberOfPages, Diagnostics: diagnostics)
                : new PdfTextExtractionResult(PdfTextExtractionStatus.Success, extracted, document.NumberOfPages, diagnostics);
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

    private static bool HasLikelyColumns(UglyToad.PdfPig.Content.Page page)
    {
        if (page.Letters.Count < 80 || page.Width <= 0 || page.Height <= 0) return false;
        var words = NearestNeighbourWordExtractor.Instance.GetWords(page.Letters)
            .Where(word => word.Text.Length > 1)
            .ToArray();
        if (words.Length < 30) return false;

        var left = words.Where(word => word.BoundingBox.Right < page.Width * .47).ToArray();
        var right = words.Where(word => word.BoundingBox.Left > page.Width * .53).ToArray();
        var crossing = words.Count(word =>
            word.BoundingBox.Left <= page.Width * .47 && word.BoundingBox.Right >= page.Width * .53);
        if (left.Length < 15 || right.Length < 15 || crossing > words.Length * .12) return false;

        static HashSet<int> VerticalBands(IEnumerable<UglyToad.PdfPig.Content.Word> values) =>
            values.Select(word => (int)Math.Floor(((word.BoundingBox.Bottom + word.BoundingBox.Top) / 2d) / 24d)).ToHashSet();
        var leftBands = VerticalBands(left);
        var rightBands = VerticalBands(right);
        var overlap = leftBands.Intersect(rightBands).Count();
        return overlap >= 4 && overlap >= Math.Min(leftBands.Count, rightBands.Count) * .3;
    }

    private static bool HasRepeatedHeaderOrFooter(IReadOnlyList<string> pageTexts, int pageCount)
    {
        if (pageCount < 2 || pageTexts.Count < 2) return false;
        var candidates = pageTexts.SelectMany(pageText =>
        {
            var lines = pageText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (lines.Length == 0) return Array.Empty<string>();
            return new[] { NormalizeRepeatedLine(lines[0]), NormalizeRepeatedLine(lines[^1]) };
        }).Where(line => line.Length is >= 3 and <= 100);
        return candidates.GroupBy(line => line, StringComparer.OrdinalIgnoreCase)
            .Any(group => group.Count() >= Math.Min(2, pageTexts.Count));
    }

    private static string NormalizeRepeatedLine(string value) =>
        System.Text.RegularExpressions.Regex.Replace(value, @"\b\d+\b", "#").Trim();
}
