namespace ApplyWise.Web.ViewModels.BestResumePicker;

public sealed record BestResumePickerResultViewModel(
    int? JobApplicationId,
    string ContextTitle,
    int? RecommendedResumeId,
    string? RecommendedResumeVersionName,
    string? RecommendationReason,
    int ComparedResumeCount,
    int ReadableResumeCount,
    bool HasDetectedSkills,
    IReadOnlyList<ComparedResumeViewModel> ComparedResumes)
{
    public ComparedResumeViewModel? RecommendedResume =>
        ComparedResumes.FirstOrDefault(resume => resume.IsRecommended);
}
