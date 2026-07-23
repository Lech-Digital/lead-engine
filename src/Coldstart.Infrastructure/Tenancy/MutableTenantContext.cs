using Coldstart.Application.Ports;

namespace Coldstart.Infrastructure.Tenancy;

// Scoped tenant holder for background hosts (harvester) and tests. Default: no tenant (fail closed).
internal sealed class MutableTenantContext : ITenantContext, ITenantScope
{
    public Guid? CustomerId { get; private set; }
    public void SetCurrent(Guid customerId) => CustomerId = customerId;
}
