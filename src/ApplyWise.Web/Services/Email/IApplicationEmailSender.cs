namespace ApplyWise.Web.Services.Email;

public interface IApplicationEmailSender
{
    Task SendAccountSecurityCodeAsync(string email, string actionLabel, string code);
}
