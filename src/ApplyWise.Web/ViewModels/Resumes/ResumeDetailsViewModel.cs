namespace ApplyWise.Web.ViewModels.Resumes;

public sealed record ResumeDetailsViewModel(int Id, string VersionName, string OriginalFileName,
    long FileSize, bool IsDefault, DateTimeOffset UploadedAt, DateTimeOffset UpdatedAt, string? Notes);
