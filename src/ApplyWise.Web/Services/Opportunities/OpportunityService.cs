using ApplyWise.Web.Data;
using ApplyWise.Web.Models;
using ApplyWise.Web.ViewModels.Opportunities;
using Microsoft.EntityFrameworkCore;

namespace ApplyWise.Web.Services.Opportunities;

public sealed class OpportunityService(ApplicationDbContext db) : IOpportunityService
{
    public async Task<OpportunityFeedViewModel> GetFeedAsync(string userId, OpportunityFeedQuery query, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var baseQuery = db.Opportunities.AsNoTracking()
            .Where(item => item.Status == OpportunityStatus.Published && (item.Deadline == null || item.Deadline >= now));
        if (query.Category.HasValue) baseQuery = baseQuery.Where(item => item.Category == query.Category);
        if (query.WorkMode.HasValue) baseQuery = baseQuery.Where(item => item.WorkMode == query.WorkMode);
        if (query.NoExperience) baseQuery = baseQuery.Where(item => item.NoExperienceRequired);
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var search = query.Search.Trim();
            baseQuery = baseQuery.Where(item => item.Title.Contains(search) || item.OrganizationName.Contains(search) ||
                (item.Skills != null && item.Skills.Contains(search)) || (item.Location != null && item.Location.Contains(search)));
        }

        var total = await baseQuery.CountAsync(cancellationToken);
        var pageSize = Math.Clamp(query.PageSize, 6, 30);
        var page = Math.Max(1, query.Page);
        var savedIds = db.SavedOpportunities.AsNoTracking().Where(saved => saved.UserId == userId).Select(saved => saved.OpportunityId);
        var items = await baseQuery
            .OrderByDescending(item => query.Sort == "closing" && item.Deadline != null)
            .ThenBy(item => query.Sort == "closing" ? item.Deadline : null)
            .ThenByDescending(item => item.IsVerified)
            .ThenByDescending(item => item.PublishedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(item => new OpportunityCardViewModel(item.Id, item.Title, item.OrganizationName, item.Category,
                item.EmploymentType, item.WorkMode, item.Location, item.Summary, item.Compensation,
                item.PublishedAt, item.Deadline, item.IsVerified, savedIds.Contains(item.Id), null))
            .ToListAsync(cancellationToken);

        return new OpportunityFeedViewModel(items, total, page, pageSize, query);
    }

    public async Task<IReadOnlyList<OpportunityCardViewModel>> GetMatchesAsync(string userId, int take, CancellationToken cancellationToken = default)
    {
        var profile = await db.CareerProfiles.AsNoTracking().SingleOrDefaultAsync(p => p.UserId == userId, cancellationToken);
        var skills = (profile?.Skills ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var interests = (profile?.OpportunityInterests ?? string.Empty).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var now = DateTimeOffset.UtcNow;
        var candidates = await db.Opportunities.AsNoTracking()
            .Where(item => item.Status == OpportunityStatus.Published && (item.Deadline == null || item.Deadline >= now))
            .OrderByDescending(item => item.PublishedAt).Take(200)
            .ToListAsync(cancellationToken);
        var savedIds = await db.SavedOpportunities.AsNoTracking().Where(s => s.UserId == userId).Select(s => s.OpportunityId).ToHashSetAsync(cancellationToken);
        return candidates.Select(item =>
        {
            var haystack = $"{item.Title} {item.Summary} {item.Skills} {item.Category}";
            var score = skills.Concat(interests).Count(term => haystack.Contains(term, StringComparison.OrdinalIgnoreCase));
            return new OpportunityCardViewModel(item.Id, item.Title, item.OrganizationName, item.Category, item.EmploymentType,
                item.WorkMode, item.Location, item.Summary, item.Compensation, item.PublishedAt, item.Deadline,
                item.IsVerified, savedIds.Contains(item.Id), score > 0 ? "Matches your profile" : "Recently posted");
        }).OrderByDescending(item => item.MatchReason == "Matches your profile").ThenByDescending(item => item.PublishedAt).Take(Math.Clamp(take, 1, 12)).ToList();
    }
}
