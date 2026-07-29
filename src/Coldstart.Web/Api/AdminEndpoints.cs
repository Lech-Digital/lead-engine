namespace Coldstart.Web.Api;

internal static class AdminEndpoints
{
    public static IEndpointRouteBuilder MapAdminApi(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/admin").AddEndpointFilter<AdminApiKeyFilter>();

        group.MapGet("/ping", () => Results.Ok(new { status = "ok" }));

        return app;
    }
}
