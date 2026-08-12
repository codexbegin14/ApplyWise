using System.IO.Compression;
using System.Text;
using ApplyWise.Web.Services.ResumeAnalysis;
using Xunit;

namespace ApplyWise.Web.Tests;

public sealed class DocxResumeExtractionTests
{
    [Fact]
    public void Docx_inspector_extracts_text_page_count_and_structure_risks()
    {
        var path = Path.Combine(Path.GetTempPath(), $"applywise-{Guid.NewGuid():N}.docx");
        try
        {
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                Write(archive, "[Content_Types].xml", "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"xml\" ContentType=\"application/xml\"/></Types>");
                Write(archive, "word/document.xml", """
                    <w:document xmlns:w="http://schemas.openxmlformats.org/wordprocessingml/2006/main">
                      <w:body><w:p><w:r><w:t>Jordan Lee</w:t></w:r></w:p><w:p><w:r><w:t>Terraform Engineer</w:t></w:r></w:p>
                      <w:tbl><w:tr><w:tc><w:p><w:r><w:t>Skills</w:t></w:r></w:p></w:tc></w:tr></w:tbl>
                      <w:sectPr><w:cols w:num="2"/></w:sectPr></w:body>
                    </w:document>
                    """);
                Write(archive, "docProps/app.xml", "<Properties xmlns=\"http://schemas.openxmlformats.org/officeDocument/2006/extended-properties\"><Pages>2</Pages></Properties>");
                Write(archive, "word/header1.xml", "<w:hdr xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:p><w:r><w:t>Jordan Lee</w:t></w:r></w:p></w:hdr>");
            }

            var result = DocxTextInspector.Inspect(path);

            Assert.Equal(PdfTextExtractionStatus.Success, result.Status);
            Assert.Contains("Terraform Engineer", result.Text);
            Assert.Equal(2, result.PageCount);
            Assert.True(result.Diagnostics?.SuspectedMultiColumn);
            Assert.True(result.Diagnostics?.HasTables);
            Assert.True(result.Diagnostics?.RepeatedHeaderOrFooter);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static void Write(ZipArchive archive, string name, string value)
    {
        var entry = archive.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false));
        writer.Write(value);
    }
}
