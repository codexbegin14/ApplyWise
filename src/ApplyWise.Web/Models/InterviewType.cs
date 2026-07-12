using System.ComponentModel.DataAnnotations;

namespace ApplyWise.Web.Models;

public enum InterviewType
{
    [Display(Name = "HR Interview")]
    HrInterview,
    [Display(Name = "Technical Interview")]
    TechnicalInterview,
    [Display(Name = "Coding Test")]
    CodingTest,
    [Display(Name = "Final Interview")]
    FinalInterview,
    [Display(Name = "Onsite Interview")]
    OnsiteInterview,
    [Display(Name = "Phone Screen")]
    PhoneScreen,
    Other
}
