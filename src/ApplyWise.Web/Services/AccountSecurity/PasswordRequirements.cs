using System.ComponentModel.DataAnnotations;

namespace ApplyWise.Web.Services.AccountSecurity;

public static class PasswordRequirements
{
    public const int MinimumLength = 12;
    public const int RequiredUniqueCharacters = 4;
}

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class StrongPasswordAttribute : ValidationAttribute
{
    public StrongPasswordAttribute()
    {
        ErrorMessage =
            $"Use at least {PasswordRequirements.MinimumLength} characters with an uppercase letter, a lowercase letter, and a number.";
    }

    public override bool IsValid(object? value)
    {
        if (value is not string password || password.Length < PasswordRequirements.MinimumLength)
        {
            return false;
        }

        return password.Any(char.IsUpper)
            && password.Any(char.IsLower)
            && password.Any(char.IsDigit)
            && password.Distinct().Take(PasswordRequirements.RequiredUniqueCharacters).Count()
                == PasswordRequirements.RequiredUniqueCharacters;
    }
}
