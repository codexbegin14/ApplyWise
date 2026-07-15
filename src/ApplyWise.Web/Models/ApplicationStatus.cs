using System.ComponentModel.DataAnnotations;

namespace ApplyWise.Web.Models;

public enum ApplicationStatus
{
    Applied = 1,
    Pending = 2,
    Interview = 3,
    Offered = 5,
    Accepted = 8,
    Rejected = 6,
    [Display(Name = "User rejected")]
    UserRejected = 7,
    Ignored = 9
}
