using Coldstart.Application.Ports;
using Coldstart.Infrastructure.Identity;
using Microsoft.AspNetCore.Http;

namespace Coldstart.Infrastructure.Tenancy;

// Reads the tenant from the signed-in principal's customer_id claim. Null when anonymous/absent.
internal sealed class HttpTenantContext(IHttpContextAccessor accessor) : ITenantContext
{
    public Guid? CustomerId
    {
        get
        {
            var value = accessor.HttpContext?.User?.FindFirst(AppUserClaimsPrincipalFactory.CustomerIdClaim)?.Value;
            return Guid.TryParse(value, out var id) ? id : null;
        }
    }
}
