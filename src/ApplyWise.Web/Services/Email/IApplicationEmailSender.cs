using ApplyWise.Web.Models;

namespace ApplyWise.Web.Services.Email;

public interface IApplicationEmailSender
{
    Task SendAccountSecurityCodeAsync(string email, AccountSecurityAction action, string code);
}
