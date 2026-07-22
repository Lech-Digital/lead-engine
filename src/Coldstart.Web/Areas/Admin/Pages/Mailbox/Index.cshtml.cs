using Coldstart.Application.Mailbox;
using Coldstart.Infrastructure.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace Coldstart.Web.Areas.Admin.Pages.Mailbox;

[Authorize(Roles = ColdstartRoles.SuperAdmin)]
public sealed class IndexModel(IMailbox mailbox, ILogger<IndexModel> logger) : PageModel
{
    public MailboxPage? Mail { get; private set; }
    public bool Unreachable { get; private set; }
    public int Page_ { get; private set; } = 1;

    public async Task OnGetAsync(int page = 1, CancellationToken ct = default)
    {
        Page_ = Math.Max(1, page);
        try
        {
            Mail = await mailbox.ListAsync(Page_, 25, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Mailbox unreachable while listing inbox.");
            Unreachable = true;
        }
    }
}
