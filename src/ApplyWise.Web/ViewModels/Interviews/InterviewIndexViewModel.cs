namespace ApplyWise.Web.ViewModels.Interviews;

public sealed class InterviewIndexViewModel
{
    public string Filter { get; set; } = "upcoming";
    public IReadOnlyList<InterviewListItemViewModel> Interviews { get; set; } = [];
}
