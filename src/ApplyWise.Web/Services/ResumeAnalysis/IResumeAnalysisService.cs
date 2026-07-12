namespace ApplyWise.Web.Services.ResumeAnalysis;

public interface IResumeAnalysisService
{
    ResumeAnalysisResult Analyze(string resumeText, string jobDescription);
}
