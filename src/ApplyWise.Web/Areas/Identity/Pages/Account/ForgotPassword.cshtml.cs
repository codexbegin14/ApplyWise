using System.ComponentModel.DataAnnotations;
using ApplyWise.Web.Models;
using ApplyWise.Web.Services.AccountSecurity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.RateLimiting;

namespace ApplyWise.Web.Areas.Identity.Pages.Account;

[EnableRateLimiting("account-security")]
public class ForgotPasswordModel(
    IAccountSecurityRequestQueue securityRequests) : PageModel
{
    [BindProperty]
    public InputModel Input { get; set; } = new();

    public sealed class InputModel
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;
    }

    public IActionResult OnPost()
    {
        Input.Email = Input.Email.Trim();
        if (!ModelState.IsValid)
        {
            return Page();
        }

        securityRequests.TryQueue(Input.Email, AccountSecurityAction.ResetPassword);

        // Always continue to the same screen so this form cannot reveal registered email addresses.
        return RedirectToPage("./ResetPassword", new { email = Input.Email });
    }
}
