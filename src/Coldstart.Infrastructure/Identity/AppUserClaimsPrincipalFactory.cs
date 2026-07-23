using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Coldstart.Infrastructure.Identity;

// Stamps the tenant onto the auth cookie at sign-in so tenant resolution is a claim read (no DB hit).
// Derives from the role-aware base (matching AddIdentity<ApplicationUser, IdentityRole>'s default
// factory) so role claims — required by [Authorize(Roles = ...)] on Admin pages — keep flowing.
public sealed class AppUserClaimsPrincipalFactory(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole> roleManager,
    IOptions<IdentityOptions> options)
    : UserClaimsPrincipalFactory<ApplicationUser, IdentityRole>(userManager, roleManager, options)
{
    public const string CustomerIdClaim = "customer_id";

    protected override async Task<ClaimsIdentity> GenerateClaimsAsync(ApplicationUser user)
    {
        var identity = await base.GenerateClaimsAsync(user);
        if (user.CustomerId is { } cid)
            identity.AddClaim(new Claim(CustomerIdClaim, cid.ToString()));
        return identity;
    }
}
