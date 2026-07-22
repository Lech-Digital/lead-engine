using Microsoft.AspNetCore.Identity;

namespace Coldstart.Infrastructure.Identity;

public sealed class ApplicationUser : IdentityUser
{
    public Guid? CustomerId { get; set; }
    public DateTimeOffset? InvitedAt { get; set; }

    // Onboarding-hint state. HintsSeenCsv = comma-separated dismissed hint keys; HintShownOn =
    // the (UTC) day the current hint was surfaced; HintDismissedOn = the (UTC) day a hint was
    // last dismissed. All three enforce "one new hint per day, stays until dismissed, next waits
    // for tomorrow" — see HintPicker.
    public string? HintsSeenCsv { get; set; }
    public DateOnly? HintShownOn { get; set; }
    public DateOnly? HintDismissedOn { get; set; }
}
