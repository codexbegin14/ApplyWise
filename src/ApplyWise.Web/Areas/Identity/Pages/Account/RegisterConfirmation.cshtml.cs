using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ApplyWise.Web.Areas.Identity.Pages.Account;

public class RegisterConfirmationModel : PageModel
{
    public string Email { get; private set; } = string.Empty;
    public string ReturnUrl { get; private set; } = "/";

    public IActionResult OnGet(string? email, string? returnUrl = null)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return RedirectToPage("./Register");
        }

        Email = email.Trim();
        ReturnUrl = !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl)
            ? returnUrl
            : Url.Content("~/");

        return Page();
    }
}
