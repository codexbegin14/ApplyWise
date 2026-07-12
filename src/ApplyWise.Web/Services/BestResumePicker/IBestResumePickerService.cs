namespace ApplyWise.Web.Services.BestResumePicker;

public interface IBestResumePickerService
{
    Task<BestResumePickerResult> CompareResumesForJobAsync(
        string userId,
        int jobApplicationId,
        CancellationToken cancellationToken = default);
}
