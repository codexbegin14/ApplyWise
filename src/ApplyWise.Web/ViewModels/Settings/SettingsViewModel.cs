using System.ComponentModel.DataAnnotations;

namespace ApplyWise.Web.ViewModels.Settings;

public sealed class SettingsViewModel
{
    public string Email { get; init; } = string.Empty;
    public bool OpportunityNotificationsEnabled { get; set; }
    public PreferencesInput Preferences { get; set; } = new();
    public ChangePasswordInput ChangePassword { get; set; } = new();
    public DeleteAccountInput DeleteAccount { get; set; } = new();
}

public sealed class PreferencesInput
{
    public bool OpportunityNotificationsEnabled { get; set; }
}

public sealed class ChangePasswordInput
{
    [Required, DataType(DataType.Password), Display(Name = "Current password")]
    public string CurrentPassword { get; set; } = string.Empty;

    [Required, StringLength(100, MinimumLength = 6), DataType(DataType.Password), Display(Name = "New password")]
    public string NewPassword { get; set; } = string.Empty;

    [Required, DataType(DataType.Password), Compare(nameof(NewPassword)), Display(Name = "Confirm new password")]
    public string ConfirmPassword { get; set; } = string.Empty;

    [Required, RegularExpression("^[0-9]{6}$", ErrorMessage = "Enter the six-digit code from your email."), Display(Name = "Email code")]
    public string Code { get; set; } = string.Empty;
}

public sealed class DeleteAccountInput
{
    [Required, RegularExpression("^[0-9]{6}$", ErrorMessage = "Enter the six-digit code from your email."), Display(Name = "Email code")]
    public string Code { get; set; } = string.Empty;

    [Required, RegularExpression("^DELETE$", ErrorMessage = "Type DELETE in capital letters to confirm account deletion."), Display(Name = "Type DELETE to confirm")]
    public string Confirmation { get; set; } = string.Empty;
}
