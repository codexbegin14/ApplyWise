using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace ApplyWise.Web.Services.ResumeAnalysis;

public static class DocxTextInspector
{
    private const int MaxEntryCharacters = 5_000_000;
    private static readonly XNamespace Word = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace Extended = "http://schemas.openxmlformats.org/officeDocument/2006/extended-properties";

    public static PdfTextExtractionResult Inspect(string filePath, CancellationToken cancellationToken = default)
    {
        try
        {
            using var archive = ZipFile.OpenRead(filePath);
            if (archive.GetEntry("[Content_Types].xml") is null || archive.GetEntry("word/document.xml") is not { } documentEntry)
                return new PdfTextExtractionResult(PdfTextExtractionStatus.Invalid);
            if (documentEntry.Length is <= 0 or > MaxEntryCharacters)
                return new PdfTextExtractionResult(PdfTextExtractionStatus.TextLimitExceeded);

            cancellationToken.ThrowIfCancellationRequested();
            var document = LoadXml(documentEntry);
            var text = ExtractParagraphText(document);
            if (text.Length > PdfTextInspector.MaxExtractedCharacters)
                return new PdfTextExtractionResult(PdfTextExtractionStatus.TextLimitExceeded);

            var pageCount = ReadPageCount(archive);
            var smallRuns = document.Descendants(Word + "sz")
                .Select(item => int.TryParse((string?)item.Attribute(Word + "val"), out var size) ? size : 0)
                .Where(size => size > 0)
                .ToArray();
            var columnCount = document.Descendants(Word + "cols")
                .Select(item => int.TryParse((string?)item.Attribute(Word + "num"), out var count) ? count : 1)
                .DefaultIfEmpty(1)
                .Max();
            var hasHeaderFooterText = archive.Entries
                .Where(entry => entry.FullName.StartsWith("word/header", StringComparison.OrdinalIgnoreCase) ||
                                entry.FullName.StartsWith("word/footer", StringComparison.OrdinalIgnoreCase))
                .Any(entry => entry.Length > 0 && entry.Length <= MaxEntryCharacters && ExtractParagraphText(LoadXml(entry)).Length > 0);

            var diagnostics = new ResumeFileDiagnostics(
                "DOCX",
                LayoutAssessed: true,
                SuspectedMultiColumn: columnCount > 1,
                RepeatedHeaderOrFooter: hasHeaderFooterText,
                HasVerySmallText: smallRuns.Length > 0 && smallRuns.Count(size => size < 16) >= Math.Max(2, smallRuns.Length / 10),
                HasTables: document.Descendants(Word + "tbl").Any(),
                HasTextBoxes: document.Descendants(Word + "txbxContent").Any(),
                Notes: ["DOCX structure was inspected directly; verify table and header content appears in the extracted reading order."]);

            return string.IsNullOrWhiteSpace(text)
                ? new PdfTextExtractionResult(PdfTextExtractionStatus.NoText, PageCount: pageCount, Diagnostics: diagnostics)
                : new PdfTextExtractionResult(PdfTextExtractionStatus.Success, text, pageCount, diagnostics);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (InvalidDataException)
        {
            return new PdfTextExtractionResult(PdfTextExtractionStatus.Invalid);
        }
        catch (XmlException)
        {
            return new PdfTextExtractionResult(PdfTextExtractionStatus.Invalid);
        }
        catch (IOException)
        {
            return new PdfTextExtractionResult(PdfTextExtractionStatus.Invalid);
        }
    }

    private static XDocument LoadXml(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var reader = XmlReader.Create(stream, new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            MaxCharactersInDocument = MaxEntryCharacters,
            XmlResolver = null
        });
        return XDocument.Load(reader, LoadOptions.None);
    }

    private static string ExtractParagraphText(XDocument document)
    {
        var output = new StringBuilder();
        foreach (var paragraph in document.Descendants(Word + "p"))
        {
            var line = new StringBuilder();
            foreach (var node in paragraph.Descendants())
            {
                if (node.Name == Word + "t") line.Append(node.Value);
                else if (node.Name == Word + "tab") line.Append('\t');
                else if (node.Name == Word + "br" || node.Name == Word + "cr") line.Append(' ');
            }
            var value = line.ToString().Trim();
            if (value.Length == 0) continue;
            if (output.Length > 0) output.AppendLine();
            output.Append(value);
        }
        return output.ToString().Trim();
    }

    private static int? ReadPageCount(ZipArchive archive)
    {
        var entry = archive.GetEntry("docProps/app.xml");
        if (entry is null || entry.Length is <= 0 or > 100_000) return null;
        var document = LoadXml(entry);
        var value = document.Descendants(Extended + "Pages").FirstOrDefault()?.Value;
        return int.TryParse(value, out var pages) && pages is > 0 and <= PdfTextInspector.MaxPages ? pages : null;
    }
}
