using Coldstart.Application.Ports;
using Coldstart.Infrastructure.Identity;
using Microsoft.AspNetCore.Http;

namespace Coldstart.Infrastructure.Tenancy;

// Request claim wins; with no HttpContext/claim (e.g. a background harvest scope) fall back to the
// scoped MutableTenantContext that ITenantScope.SetCurrent writes.
internal sealed class HttpTenantContext(IHttpContextAccessor accessor, MutableTenantContext fallback) : ITenantContext
{
    public Guid? CustomerId
    {
        get
        {
            var value = accessor.HttpContext?.User?.FindFirst(AppUserClaimsPrincipalFactory.CustomerIdClaim)?.Value;
            if (Guid.TryParse(value, out var id)) return id;
            return fallback.CustomerId;
        }
    }
}
