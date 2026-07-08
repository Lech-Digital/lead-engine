using System.Text.RegularExpressions;
using Coldstart.Application.Customers;
using Coldstart.Infrastructure.Identity;
using Coldstart.Web;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Coldstart.Web.Tests;

// Test-only helpers for creating accounts and driving the auth forms, now that public
// registration is gone. Users are created directly via UserManager with a known password.
internal static class TestAuth
{
    internal static async Task CreateUserAsync(ColdstartWebFactory factory, string email, string password,
        string? role = null, bool provisionTenant = true)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        if (await users.FindByEmailAsync(email) is not null) return;
        var user = new ApplicationUser { UserName = email, Email = email, EmailConfirmed = true };
        await users.CreateAsync(user, password);
        if (provisionTenant)
        {
            var provision = scope.ServiceProvider.GetRequiredService<IProvisionCustomer>();
            user.CustomerId = await provision.ProvisionAsync(email, CancellationToken.None);
            await users.UpdateAsync(user);
        }
        if (role is not null) await users.AddToRoleAsync(user, role);
    }

    // Reads a page, pulls its antiforgery token (Razor Pages emits one for every method=post form).
    internal static async Task<string?> AntiforgeryAsync(HttpClient client, string path)
    {
        var html = await client.GetStringAsync(path);
        var m = Regex.Match(html, "name=\"__RequestVerificationToken\"[^>]*value=\"([^\"]+)\"");
        return m.Success ? m.Groups[1].Value : null;
    }

    internal static async Task<HttpResponseMessage> PostFormAsync(HttpClient client, string path,
        Dictionary<string, string> fields)
    {
        var token = await AntiforgeryAsync(client, path);
        if (token is not null) fields["__RequestVerificationToken"] = token;
        return await client.PostAsync(path, new FormUrlEncodedContent(fields));
    }

    internal static async Task<string> PostFormReadAsync(HttpClient client, string path,
        Dictionary<string, string> fields)
    {
        var res = await PostFormAsync(client, path, fields);
        return await res.Content.ReadAsStringAsync();
    }

    // Creates the account (known password, confirmed, provisioned tenant unless role is SuperAdmin)
    // then signs `client` in through the Login form — the replacement for the removed Register flow.
    internal static async Task SignInAsync(ColdstartWebFactory factory, HttpClient client,
        string email, string password, string? role = null)
    {
        await CreateUserAsync(factory, email, password, role, provisionTenant: role != ColdstartRoles.SuperAdmin);
        await PostFormAsync(client, "/Identity/Account/Login", new()
        {
            ["Input.Email"] = email,
            ["Input.Password"] = password,
            ["Input.RememberMe"] = "false",
        });
    }
}
