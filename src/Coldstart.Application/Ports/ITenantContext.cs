namespace Coldstart.Application.Ports;

// Ambient current tenant. Null => no tenant established (anonymous/system/background before scoping);
// filtered reads then return nothing (fail closed).
public interface ITenantContext
{
    Guid? CustomerId { get; }
}

// Write side: background/host code sets the current tenant for the remainder of the scope.
public interface ITenantScope
{
    void SetCurrent(Guid customerId);
}
