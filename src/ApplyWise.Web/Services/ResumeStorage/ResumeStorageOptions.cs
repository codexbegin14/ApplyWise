namespace ApplyWise.Web.Services.ResumeStorage;

public sealed class ResumeStorageOptions
{
    public const string SectionName = "ResumeStorage";

    public string RootPath { get; set; } = Path.Combine("App_Data", "Uploads", "Resumes");
}
