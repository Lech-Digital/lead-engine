using Coldstart.Application.Ports;
using Coldstart.Infrastructure.Tenancy;
using FluentAssertions;
using Xunit;

namespace Coldstart.Infrastructure.Tests;

public class MutableTenantContextTests
{
    [Fact]
    public void Default_HasNoTenant()
    {
        // Arrange / Act
        var ctx = new MutableTenantContext();

        // Assert
        ctx.CustomerId.Should().BeNull();
    }

    [Fact]
    public void SetCurrent_MakesTenantVisibleThroughReadInterface()
    {
        // Arrange
        var ctx = new MutableTenantContext();
        var id = Guid.NewGuid();

        // Act
        ((ITenantScope)ctx).SetCurrent(id);

        // Assert
        ((ITenantContext)ctx).CustomerId.Should().Be(id);
    }
}
