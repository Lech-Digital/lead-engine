namespace Coldstart.Web.Areas.App.Pages;

// Portal language for the engine's Fit verdict, not enum shouting: customers read
// "Strong fit" / "Worth a look", never the raw "Yes"/"Maybe" domain values. Used by
// the briefing queue and the lead detail header (both render Fit as a .pz-pill).
internal static class FitLabel
{
    internal static string Text(string fit) => fit switch
    {
        "Yes" => "Strong fit",
        "Maybe" => "Worth a look",
        _ => fit,
    };

    internal static string PillClass(string fit) => fit switch
    {
        "Yes" => "pz-pill-yes",
        "Maybe" => "pz-pill-maybe",
        _ => "pz-pill-sent",
    };
}
