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
                _ => UsageError(stderr),
            };
        }
        catch (HttpRequestException ex)
        {
            await stderr.WriteLineAsync($"Cannot reach the Coldstart API: {ex.Message}");
            return 1;
        }
    }

    private static int UsageError(TextWriter stderr)
    {
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
