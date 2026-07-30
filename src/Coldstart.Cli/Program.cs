using Coldstart.Cli;

var url = Environment.GetEnvironmentVariable("COLDSTART_URL") ?? "http://localhost:5000";
var key = Environment.GetEnvironmentVariable("COLDSTART_ADMIN_KEY");
if (string.IsNullOrWhiteSpace(key))
{
    Console.Error.WriteLine("COLDSTART_ADMIN_KEY is not set. Export it (and COLDSTART_URL for remote targets) first.");
    return 2;
}

using var http = new HttpClient { BaseAddress = new Uri(url), Timeout = TimeSpan.FromSeconds(30) };
http.DefaultRequestHeaders.Add("X-Admin-Key", key);
return await CommandRunner.RunAsync(args, new AdminApiClient(http), Console.Out, Console.Error);
