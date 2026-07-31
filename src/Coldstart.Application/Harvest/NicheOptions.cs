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
    public AdzunaOptions Adzuna { get; set; } = new();
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
    public double BaseScore { get; set; } = 0.45;
    public double ScoreWeight { get; set; } = 1.0;
}
