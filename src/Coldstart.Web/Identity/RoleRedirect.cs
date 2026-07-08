using Coldstart.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;

namespace Coldstart.Web;

internal static class RoleRedirect
{
    // SuperAdmins land on /admin; everyone else on /app.
    internal static async Task<string> PathForAsync(UserManager<ApplicationUser> users, ApplicationUser user, CancellationToken ct) =>
        await users.IsInRoleAsync(user, ColdstartRoles.SuperAdmin) ? "/admin" : "/app";
}
