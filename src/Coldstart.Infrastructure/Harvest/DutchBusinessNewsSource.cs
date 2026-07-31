using Coldstart.Application.Harvest;

namespace Coldstart.Infrastructure.Harvest;

// Dutch business/startup news feeds (Silicon Canals, Emerce, MT/Sprout) as their own
// selectable source, so NL-focused customers can go local-only.
internal sealed class DutchBusinessNewsSource(HttpClient http, NicheOptions niche)
    : FundingRssSource(http, niche.FundingNl, "funding-nl");
