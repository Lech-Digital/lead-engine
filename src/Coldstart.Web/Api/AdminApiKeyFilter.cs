using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace Coldstart.Web.Api;

// Gate for /api/admin/*: constant-time X-Admin-Key check against AdminApi:Key.
// Fail closed: no configured key -> 404 (the feature is off, not open).
internal sealed class AdminApiKeyFilter(IOptions<AdminApiOptions> options) : IEndpointFilter
{
    public const string HeaderName = "X-Admin-Key";

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        var configured = options.Value.Key;
        if (string.IsNullOrWhiteSpace(configured))
            return Results.NotFound();

        var presented = context.HttpContext.Request.Headers[HeaderName].ToString();
        if (presented.Length == 0 || !FixedTimeEquals(presented, configured))
            return Results.Unauthorized();

        return await next(context);
    }

    private static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
}
