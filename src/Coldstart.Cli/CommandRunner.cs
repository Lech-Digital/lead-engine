using System.Text.Json;

namespace Coldstart.Cli;

internal static class CommandRunner
{
    private const string Usage =
        """
        Usage: coldstart <command> [options]

        Commands:
          ping                         Check the admin API is up and the key works
          stats                        Health + customer/user/invite counts
          customers                    Companies, users, plans, leads, harvest state
          invite <email> --company <name> --niche <text> [--info <text>]
          resend <email>               Re-send a pending invite
          revoke <email>               Revoke a pending invite (deletes user + tenant)
          block <email> / unblock <email>
          remove <email> --yes         Delete a user AND their tenant data
          harvest <company|id>         Enqueue a harvest for one customer
          harvest --due                Run the nightly top-up sweep now
          comp <company|id> --days <n> Grant complimentary full plan for n days (emails the customer)

        Options:
          --json                       Print the raw API response (read commands)
        """;

    public static async Task<int> RunAsync(string[] args, AdminApiClient api, TextWriter stdout, TextWriter stderr)
    {
        var json = args.Contains("--json");
        var positional = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToArray();

        try
        {
            return positional switch
            {
                ["ping"] => await ReadAsync(api.GetAsync("/api/admin/ping"), json, stdout, stderr, _ => "ok"),
                ["stats"] => await ReadAsync(api.GetAsync("/api/admin/stats"), json, stdout, stderr, RenderStats),
                ["customers"] => await ReadAsync(api.GetAsync("/api/admin/customers"), json, stdout, stderr, RenderCustomers),

                ["invite", var email, ..] when GetOption(args, "--company") is { } company
                        && GetOption(args, "--niche") is { } niche =>
                    await ActAsync(api.PostAsync("/api/admin/invites",
                        new { email, company, niche, info = GetOption(args, "--info") }), stdout, stderr),
                ["invite", ..] => UsageError(stderr, "invite requires <email> --company <name> --niche <text>."),

                ["resend", var email] => await ActAsync(api.PostAsync($"/api/admin/invites/{Uri.EscapeDataString(email)}/resend"), stdout, stderr),
                ["revoke", var email] => await ActAsync(api.DeleteAsync($"/api/admin/invites/{Uri.EscapeDataString(email)}"), stdout, stderr),
                ["block", var email] => await ActAsync(api.PostAsync($"/api/admin/users/{Uri.EscapeDataString(email)}/block"), stdout, stderr),
                ["unblock", var email] => await ActAsync(api.PostAsync($"/api/admin/users/{Uri.EscapeDataString(email)}/unblock"), stdout, stderr),
                ["remove", var email] when args.Contains("--yes") =>
                    await ActAsync(api.DeleteAsync($"/api/admin/users/{Uri.EscapeDataString(email)}"), stdout, stderr),
                ["remove", ..] => UsageError(stderr, "remove deletes the user AND their tenant data — re-run with --yes."),

                ["harvest"] when args.Contains("--due") =>
                    await ActAsync(api.PostAsync("/api/admin/harvest/due"), stdout, stderr),
                ["harvest", var target] => await HarvestAsync(target, api, stdout, stderr),

                ["comp", var target, ..] when GetOption(args, "--days") is { } daysRaw && int.TryParse(daysRaw, out var days) =>
                    await CompAsync(target, days, api, stdout, stderr),
                ["comp", ..] => UsageError(stderr, "comp requires <company|id> --days <n>."),

                _ => UsageError(stderr),
            };
        }
        catch (HttpRequestException ex)
        {
            await stderr.WriteLineAsync($"Cannot reach the Coldstart API: {ex.Message}");
            return 1;
        }
        catch (OperationCanceledException)
        {
            await stderr.WriteLineAsync("The Coldstart API did not respond within 30 seconds.");
            return 1;
        }
    }

    private static int UsageError(TextWriter stderr, string? message = null)
    {
        if (message is not null) stderr.WriteLine(message);
        stderr.WriteLine(Usage);
        return 2;
    }

    private static async Task<int> ReadAsync(Task<ApiResult> call, bool json,
        TextWriter stdout, TextWriter stderr, Func<JsonElement, string> render)
    {
        var result = await call;
        if (!result.Ok)
        {
            await stderr.WriteLineAsync($"HTTP {result.Status}{(result.Body.Length > 0 ? ": " + result.Body : "")}");
            return 1;
        }
        if (json)
        {
            await stdout.WriteLineAsync(result.Body);
            return 0;
        }
        using var doc = JsonDocument.Parse(result.Body);
        await stdout.WriteLineAsync(render(doc.RootElement));
        return 0;
    }

    // --option value (the token after the flag)
    private static string? GetOption(string[] args, string name)
    {
        var i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    // Mutations: print the API's message (or raw body) on success.
    private static async Task<int> ActAsync(Task<ApiResult> call, TextWriter stdout, TextWriter stderr)
    {
        var result = await call;
        if (!result.Ok)
        {
            await stderr.WriteLineAsync($"HTTP {result.Status}{(result.Body.Length > 0 ? ": " + result.Body : "")}");
            return 1;
        }
        await stdout.WriteLineAsync(result.Body);
        return 0;
    }

    private static async Task<int> HarvestAsync(string target, AdminApiClient api, TextWriter stdout, TextWriter stderr)
    {
        var (id, error) = await ResolveCustomerIdAsync(target, api);
        if (error is not null)
        {
            await stderr.WriteLineAsync(error);
            return 1;
        }
        return await ActAsync(api.PostAsync($"/api/admin/harvest/{id}"), stdout, stderr);
    }

    private static async Task<int> CompAsync(string target, int days, AdminApiClient api, TextWriter stdout, TextWriter stderr)
    {
        var (id, error) = await ResolveCustomerIdAsync(target, api);
        if (error is not null)
        {
            await stderr.WriteLineAsync(error);
            return 1;
        }
        return await ActAsync(api.PostAsync($"/api/admin/customers/{id}/complimentary", new { days }), stdout, stderr);
    }

    // Resolves a company name (case-insensitive) or a raw guid to a customer id via GET /api/admin/customers.
    private static async Task<(Guid Id, string? Error)> ResolveCustomerIdAsync(string target, AdminApiClient api)
    {
        if (Guid.TryParse(target, out var id))
            return (id, null);

        var customers = await api.GetAsync("/api/admin/customers");
        if (!customers.Ok)
            return (Guid.Empty, $"HTTP {customers.Status}: {customers.Body}");

        using var doc = JsonDocument.Parse(customers.Body);
        var match = doc.RootElement.EnumerateArray().FirstOrDefault(c =>
            string.Equals(c.GetProperty("name").GetString(), target, StringComparison.OrdinalIgnoreCase));
        if (match.ValueKind != JsonValueKind.Object)
            return (Guid.Empty, $"No customer named '{target}'. Run `coldstart customers` to see names.");

        return (match.GetProperty("id").GetGuid(), null);
    }

    private static string RenderStats(JsonElement root) => Table.Render(
        ["DATABASE", "CUSTOMERS", "USERS", "PENDING INVITES", "BLOCKED"],
        [[
            root.GetProperty("database").GetString() ?? "",
            root.GetProperty("customers").GetInt32().ToString(),
            root.GetProperty("users").GetInt32().ToString(),
            root.GetProperty("pendingInvites").GetInt32().ToString(),
            root.GetProperty("blockedUsers").GetInt32().ToString(),
        ]]);

    private static string RenderCustomers(JsonElement root)
    {
        var rows = new List<string[]>();
        foreach (var c in root.EnumerateArray())
        {
            var users = string.Join(", ", c.GetProperty("users").EnumerateArray()
                .Select(u => $"{u.GetProperty("email").GetString()} ({u.GetProperty("status").GetString()})"));
            var lastRun = c.GetProperty("lastHarvestAt").ValueKind == JsonValueKind.Null
                ? "—" : c.GetProperty("lastHarvestAt").GetString()!;
            rows.Add([
                c.GetProperty("name").GetString() ?? "",
                c.GetProperty("plan").GetString() ?? "",
                $"{c.GetProperty("leadsPendingReview").GetInt32()}/{c.GetProperty("leadsTotal").GetInt32()}",
                c.GetProperty("harvestStatus").GetString() ?? "",
                lastRun,
                users,
            ]);
        }
        return Table.Render(["COMPANY", "PLAN", "LEADS", "HARVEST", "LAST RUN", "USERS"], rows);
    }
}
