using ApplyWise.Web.ViewModels.Opportunities;

namespace ApplyWise.Web.Services.Opportunities;

public interface IOpportunityService
{
    Task<OpportunityFeedViewModel> GetFeedAsync(string userId, OpportunityFeedQuery query, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OpportunityCardViewModel>> GetMatchesAsync(string userId, int take, CancellationToken cancellationToken = default);
}
