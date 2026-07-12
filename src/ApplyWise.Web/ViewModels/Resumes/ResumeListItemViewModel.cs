namespace ApplyWise.Web.ViewModels.Resumes;

public sealed record ResumeListItemViewModel(int Id, string VersionName, string OriginalFileName,
    long FileSize, bool IsDefault, DateTimeOffset UploadedAt, string? Notes);
