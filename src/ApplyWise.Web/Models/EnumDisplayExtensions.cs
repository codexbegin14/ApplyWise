using System.ComponentModel.DataAnnotations;
using System.Reflection;

namespace ApplyWise.Web.Models;

public static class EnumDisplayExtensions
{
    public static string GetDisplayName(this Enum value) =>
        value.GetType().GetMember(value.ToString()).First()
            .GetCustomAttribute<DisplayAttribute>()?.GetName() ?? value.ToString();
}
