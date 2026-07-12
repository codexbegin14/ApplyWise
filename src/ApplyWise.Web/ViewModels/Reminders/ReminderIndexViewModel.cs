namespace ApplyWise.Web.ViewModels.Reminders;

public sealed class ReminderIndexViewModel
{
    public string Filter { get; set; } = "pending";
    public IReadOnlyList<ReminderListItemViewModel> Reminders { get; set; } = [];
}
