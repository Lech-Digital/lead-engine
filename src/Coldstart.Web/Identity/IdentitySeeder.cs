using System.Security.Cryptography;
using Coldstart.Infrastructure.Identity;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Coldstart.Web;

public static class IdentitySeeder
{
    public static async Task SeedAsync(IServiceProvider services, CancellationToken ct)
    {
        var roles = services.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in ColdstartRoles.All)
            if (!await roles.RoleExistsAsync(role))
                await roles.CreateAsync(new IdentityRole(role));

        var users = services.GetRequiredService<UserManager<ApplicationUser>>();
        if (await users.FindByEmailAsync(ColdstartRoles.SuperAdminEmail) is null)
        {
            var admin = new ApplicationUser
            {
                UserName = ColdstartRoles.SuperAdminEmail,
                Email = ColdstartRoles.SuperAdminEmail,
                EmailConfirmed = true,   // trusted, known identity; sets password via Forgot password
            };
            // Random, unusable password — the operator sets a real one via the reset flow.
            await users.CreateAsync(admin, "Aa1!" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)));
            await users.AddToRoleAsync(admin, ColdstartRoles.SuperAdmin);
        }
    }
}
