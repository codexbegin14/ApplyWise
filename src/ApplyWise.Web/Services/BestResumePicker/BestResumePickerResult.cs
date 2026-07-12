namespace ApplyWise.Web.Services.BestResumePicker;

public sealed record BestResumePickerResult(
    int? JobApplicationId,
    string ContextTitle,
    int? RecommendedResumeId,
    string? RecommendedResumeVersionName,
    string? RecommendationReason,
    int ComparedResumeCount,
    int ReadableResumeCount,
    bool HasDetectedSkills,
    IReadOnlyList<ComparedResumeResult> ComparedResumes);
