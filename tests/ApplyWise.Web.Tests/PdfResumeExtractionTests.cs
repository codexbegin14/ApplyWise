using ApplyWise.Web.Services.ResumeAnalysis;
using ApplyWise.Web.Services.ResumeStorage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using Xunit;

namespace ApplyWise.Web.Tests;

public sealed class PdfResumeExtractionTests
{
    private const string EncryptedPdfBase64 =
        "JVBERi0xLjYKJb/3ov4KMSAwIG9iago8PCAvUGFnZXMgMyAwIFIgL1R5cGUgL0NhdGFsb2cgPj4KZW5kb2JqCjIgMCBvYmoKPDwgL0tleXdvcmRzICj//oAfA3n4fcVrxERQR1CpWaev35fIGk4f9gl1XG6LuXxccvxErJp11Y/BZPG8E4mcV0ASlk2g1OqSiKS3uDJ1KSAvUHJvZHVjZXIgPGFmYWZkMzE4MjU3MWU2PiAvVGl0bGUgPGFmYWZkMzE4NDI1MGU3NzJkNTI2Y2E0ZjVkPiA+PgplbmRvYmoKMyAwIG9iago8PCAvQ291bnQgMSAvS2lkcyBbIDQgMCBSIF0gL1R5cGUgL1BhZ2VzID4+CmVuZG9iago0IDAgb2JqCjw8IC9Db250ZW50cyA1IDAgUiAvTWVkaWFCb3ggWyAwIDAgNjEyIDc5MiBdIC9QYXJlbnQgMyAwIFIgL1Jlc291cmNlcyA8PCAvRm9udCA8PCAvRjEgNiAwIFIgPj4gPj4gL1R5cGUgL1BhZGUgPj4KZW5kb2JqCjUgMCBvYmoKPDwgL0xlbmd0aCA0OCAvRmlsdGVyIC9GbGF0ZURlY29kZSA+PgpzdHJlYW0KZ92d/usiiWknnHIAJqEQ5EzajFw+BOxo5hVgJZ5tH1067UO1Ae14h5uZqfUlS4plZW5kc3RyZWFtCmVuZG9iago2IDAgb2JqCjw8IC9CYXNlRm9udCAvSGVsdmV0aWNhIC9TdWJ0eXBlIC9UeXBlMSAvVHlwZSAvRm9udCA+PgplbmRvYmoKNyAwIG9iago8PCAvRmlsdGVyIC9TdGFuZGFyZCAvTGVuZ3RoIDEyOCAvTyA8MzY0NTFiZDM5ZDc1M2I3YzFkMTA5MjJjMjhlNjY2NWFhNGYzMzUzZmIwMzQ4YjUzNjg5M2UzYjFkYjVjNTc5Yj4gL1AgLTQgL1IgMyAvVSA8MWM3MzU3YjMxNDJiOTMzNjRjOTE5ZTUzMTA5ZWQ1MDcwMTIyNDU2YTkxYmFlNTEzNDI3M2E2ZGIxMzRjODdjND4gL1YgMiA+PgplbmRvYmoKeHJlZgowIDgKMDAwMDAwMDAwMCA2NTUzNSBmIAowMDAwMDAwMDE1IDAwMDAwIG4gCjAwMDAwMDAwNjQgMDAwMDAgbiAKMDAwMDAwMDI4NSAwMDAwMCBuIAowMDAwMDAwMzQ0IDAwMDAwIG4gCjAwMDAwMDQ3MiAwMDAwMCBuIAowMDAwMDAwNTkwIDAwMDAwIG4gCjAwMDAwMDY2MCAwMDAwMCBuIAp0cmFpbGVyIDw8IC9JbmZvIDIgMCBSIC9Sb290IDEgMCBSIC9TaXplIDggL0lEIFs8MzkxOTM2YzQwYzRlMWY1MjYzODdkNDFlNDY3NmQ0MWI+PDM5MTkzNmM0MGM0ZTFmNTI2Mzg3ZDQxZTQ2NzZkNDFiPl0gL0VuY3J5cHQgNyAwIFIgPj4Kc3RhcnR4cmVmCjg2NwolJUVPRgo=";

    [Fact]
    public async Task Legitimate_one_page_resume_is_extracted_in_process()
    {
        var path = WriteTemporaryPdf(CreatePdf("Awais Shaikh - ASP.NET Core Developer"));
        try
        {
            var result = await CreateExtractor().InspectAsync(path);

            Assert.Equal(PdfTextExtractionStatus.Success, result.Status);
            Assert.Contains("ASP.NET Core Developer", result.Text, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Valid_image_only_style_pdf_is_accepted_without_extracted_text()
    {
        var path = WriteTemporaryPdf(CreatePdf());
        try
        {
            var result = await CreateExtractor().InspectAsync(path);

            Assert.Equal(PdfTextExtractionStatus.NoText, result.Status);
            Assert.True(result.IsValidDocument);
            Assert.Null(result.Text);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Malformed_pdf_is_rejected()
    {
        var path = Path.Combine(Path.GetTempPath(), $"applywise-test-{Guid.NewGuid():N}.pdf");
        await File.WriteAllTextAsync(path, "%PDF-1.7\nThis is not a PDF document.\n%%EOF");
        try
        {
            var result = await CreateExtractor().InspectAsync(path);

            Assert.Equal(PdfTextExtractionStatus.Invalid, result.Status);
            Assert.False(result.IsValidDocument);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Encrypted_pdf_is_rejected()
    {
        var path = WriteTemporaryPdf(Convert.FromBase64String(EncryptedPdfBase64));
        try
        {
            var result = await CreateExtractor().InspectAsync(path);

            Assert.Equal(PdfTextExtractionStatus.Encrypted, result.Status);
            Assert.False(result.IsValidDocument);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Pdf_over_page_limit_is_rejected()
    {
        var path = WriteTemporaryPdf(CreatePdf(pageCount: PdfTextInspector.MaxPages + 1));
        try
        {
            var result = await CreateExtractor().InspectAsync(path);

            Assert.Equal(PdfTextExtractionStatus.PageLimitExceeded, result.Status);
            Assert.False(result.IsValidDocument);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static ResumeTextExtractorService CreateExtractor() => new(
        Options.Create(new ResumeStorageOptions
        {
            MaxFileSizeBytes = 5 * 1024 * 1024,
            ExtractionTimeoutSeconds = 15
        }),
        NullLogger<ResumeTextExtractorService>.Instance);

    private static byte[] CreatePdf(string? text = null, int pageCount = 1)
    {
        using var builder = new PdfDocumentBuilder();
        PdfDocumentBuilder.AddedFont? font = null;
        if (text is not null)
        {
            font = builder.AddStandard14Font(Standard14Font.Helvetica);
        }

        for (var pageNumber = 0; pageNumber < pageCount; pageNumber++)
        {
            var page = builder.AddPage(PageSize.A4, isPortrait: true);
            if (text is not null)
            {
                page.AddText(text, 12, new PdfPoint(48, 780), font!);
            }
        }

        return builder.Build();
    }

    private static string WriteTemporaryPdf(byte[] bytes)
    {
        var path = Path.Combine(Path.GetTempPath(), $"applywise-test-{Guid.NewGuid():N}.pdf");
        File.WriteAllBytes(path, bytes);
        return path;
    }
}
