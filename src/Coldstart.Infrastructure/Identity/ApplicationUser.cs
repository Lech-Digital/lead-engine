using Microsoft.AspNetCore.Identity;

namespace Coldstart.Infrastructure.Identity;

public sealed class ApplicationUser : IdentityUser
{
    public Guid? CustomerId { get; set; }
    public DateTimeOffset? InvitedAt { get; set; }
}
