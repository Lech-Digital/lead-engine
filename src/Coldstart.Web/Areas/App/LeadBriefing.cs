namespace Coldstart.Web.Areas.App;

// Single source of truth for how many unfinished leads the briefing queue shows;
// History's "older" filter is exactly the unfinished leads beyond this cap.
internal static class LeadBriefing
{
    public const int QueueCap = 15;
}
