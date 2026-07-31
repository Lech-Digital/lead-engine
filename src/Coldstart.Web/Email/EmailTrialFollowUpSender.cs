using Coldstart.Application.FollowUps;
using Coldstart.Application.Ports;
using Coldstart.Infrastructure;
using Coldstart.Infrastructure.Notifications;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Coldstart.Web.Email;

// Email edition of the trial nudges. Sends to the tenant's confirmed account address;
// returns false when there is none so the use case can retry on a later run.
internal sealed class EmailTrialFollowUpSender(
    ColdstartDbContext db,
    IEmailSender email,
    IDataProtectionProvider dataProtection,
    IOptions<NotificationsOptions> options) : ITrialFollowUpSender
{
    // Purpose string is part of the key derivation — must match the opt-out endpoint.
    internal const string OptOutPurpose = "Coldstart.FollowUps.OptOut";

    public async Task<bool> SendAsync(TrialFollowUp followUp, CancellationToken ct)
    {
        var to = await db.Users
            .Where(u => u.CustomerId == followUp.CustomerId && u.Email != null && u.EmailConfirmed)
            .OrderBy(u => u.Email)
            .Select(u => u.Email!)
            .FirstOrDefaultAsync(ct);
        if (to is null) return false;

        var baseUrl = options.Value.BaseUrl.TrimEnd('/');
        var token = dataProtection.CreateProtector(OptOutPurpose).Protect(followUp.CustomerId.ToString());
        var optOutUrl = $"{baseUrl}/followups/optout?token={Uri.EscapeDataString(token)}";
        var appUrl = $"{baseUrl}/app";

        var content = followUp.Stage == TrialFollowUpStage.Day5
            ? EmailTemplates.TrialFollowUpDay5(followUp.Company, followUp.LockedLeadCount, appUrl, optOutUrl)
            : EmailTemplates.TrialFollowUpDay2(followUp.Company, followUp.LockedLeadCount, appUrl, optOutUrl);
        await email.SendAsync(to, content.Subject, content.TextBody, content.HtmlBody, ct);
        return true;
    }
}
