using System.Security.Cryptography;
using Coldstart.Application.Customers;
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
        var admin = await users.FindByEmailAsync(ColdstartRoles.SuperAdminEmail);
        if (admin is null)
        {
            admin = new ApplicationUser
            {
                UserName = ColdstartRoles.SuperAdminEmail,
                Email = ColdstartRoles.SuperAdminEmail,
                EmailConfirmed = true,   // trusted, known identity; sets password via Forgot password
            };
            // Random, unusable password — the operator sets a real one via the reset flow.
            await users.CreateAsync(admin, "Aa1!" + Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)));
        }
        if (!await users.IsInRoleAsync(admin, ColdstartRoles.SuperAdmin))
            await users.AddToRoleAsync(admin, ColdstartRoles.SuperAdmin);

        // The operator dogfoods Coldstart: link the account to a "Coldstart" tenant so it has its
        // own briefing/leads (and can send from the lead detail like any customer).
        if (admin.CustomerId is null)
        {
            var provision = services.GetRequiredService<IProvisionCustomer>();
            admin.CustomerId = await provision.ProvisionAsync("Coldstart", ct);
            await users.UpdateAsync(admin);
        }
    }
}
