namespace Coldstart.Application.Harvest;

public sealed class NicheOptions
{
    public string Offer { get; set; } = "";
    public string SenderName { get; set; } = "";
    public string SenderTitle { get; set; } = "";
    public List<string> TargetKeywords { get; set; } = [];
    public List<string> BuyingSignals { get; set; } = [];
    public List<string> Disqualifiers { get; set; } = [];
    public double MinScore { get; set; } = 0.4;
    public int MaxEnrichPerRun { get; set; } = 30;
    public string TargetCustomerName { get; set; } = "Coldstart";
    public HackerNewsOptions HackerNews { get; set; } = new();
    public FundingOptions Funding { get; set; } = new();
    // The Dutch feed set, exposed to customers as its own source ("funding-nl").
    public FundingOptions FundingNl { get; set; } = new();
    public AdzunaOptions Adzuna { get; set; } = new();
    public WebSearchOptions WebSearch { get; set; } = new();
    public LangSearchOptions LangSearch { get; set; } = new();
}

public sealed class WebSearchOptions
{
    public bool Enabled { get; set; } = true;
    // Scrapingdog API key (free tier: 200 credits/month, 5 per search → ~40 searches);
    // empty = source off (soft-degrade).
    public string ApiKey { get; set; } = "";
    public string Country { get; set; } = "nl";        // result country bias
    public string Freshness { get; set; } = "qdr:w";   // Google tbs syntax: past week — signals, not archaeology
    // The free credit budget only stretches to ONE search a night; the keyword window
    // rotates by day so the whole profile is still covered across a week.
    public int MaxQueries { get; set; } = 1;
    public int ResultsPerQuery { get; set; } = 10;
    // Below the score gate on purpose: a search hit must ALSO match profile keywords in its
    // own text to survive the deterministic filter.
    public double BaseScore { get; set; } = 0.35;
    public double ScoreWeight { get; set; } = 0.8;
}

public sealed class LangSearchOptions
{
    public bool Enabled { get; set; } = true;
    // LangSearch API key (free, unmetered); empty = source off (soft-degrade).
    public string ApiKey { get; set; } = "";
    public int MaxQueries { get; set; } = 6;
    public int Count { get; set; } = 10;   // results per query (API max 10)
    // Result dates are unreliable (the freshness filter is decorative), so hits are a
    // weaker signal than the week-fresh websearch source — scored below it on purpose.
    public double BaseScore { get; set; } = 0.25;
    public double ScoreWeight { get; set; } = 0.7;
    // Off switch for the secondary-context lookup (an extra snippet fetched per lead before
    // enrichment). On by default; operators can disable it without touching the API key.
    public bool SecondaryContextEnabled { get; set; } = true;
}

public sealed class HackerNewsOptions
{
    public bool Enabled { get; set; } = true;
    public List<string> Queries { get; set; } = [];
    public int MaxPerQuery { get; set; } = 30;
    public int LookbackHours { get; set; } = 336;
    public double ScoreWeight { get; set; } = 0.7;
}

public sealed class FundingOptions
{
    public bool Enabled { get; set; } = true;
    public List<string> Feeds { get; set; } = [];
    public int LookbackDays { get; set; } = 45;
    public int MaxPerFeed { get; set; } = 40;
    public bool RequireFundingTerms { get; set; } = true;
    public double BaseScore { get; set; } = 0.5;
}

public sealed class AdzunaOptions
{
    public bool Enabled { get; set; } = true;
    // Free developer credentials from developer.adzuna.com; empty = source off (soft-degrade).
    public string AppId { get; set; } = "";
    public string AppKey { get; set; } = "";
    public string Country { get; set; } = "nl";
    public int MaxQueries { get; set; } = 5;
    public int ResultsPerQuery { get; set; } = 20;
    public int MaxDaysOld { get; set; } = 14;
    // Deliberately BELOW the score gate: Adzuna matching is fuzzy, so a job must also
    // hit the profile keywords in its text to survive the deterministic filter.
    public double BaseScore { get; set; } = 0.35;
    public double ScoreWeight { get; set; } = 1.0;
}
