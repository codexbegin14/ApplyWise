namespace ApplyWise.Web.Services.ResumeStorage;

public sealed class ResumeStorageOptions
{
    public const string SectionName = "ResumeStorage";

    public string RootPath { get; set; } = Path.Combine("App_Data", "Uploads", "Resumes");
    public long MaxFileSizeBytes { get; set; } = 5 * 1024 * 1024;
    public int MaxFilesPerUser { get; set; } = 25;
    public long MaxBytesPerUser { get; set; } = 50 * 1024 * 1024;
    public int ExtractionTimeoutSeconds { get; set; } = 15;
}
