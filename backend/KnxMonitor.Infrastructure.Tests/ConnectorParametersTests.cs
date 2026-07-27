using System.Net;
using FluentAssertions;
using Knx.Falcon;
using Knx.Falcon.Configuration;
using KnxMonitor.Core.Entities;
using KnxMonitor.Core.Enums;
using KnxMonitor.Core.Interfaces;
using KnxMonitor.Infrastructure.KnxConnection;
using KnxMonitor.Infrastructure.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace KnxMonitor.Infrastructure.Tests;

/// <summary>
/// Covers the mapping from a stored <see cref="KnxConfiguration"/> to the Falcon connector
/// parameters. Routing was reachable in the backend long before the settings UI could select it,
/// so it never had coverage: the port was dropped on the floor and telegrams went out under
/// Falcon's 0.0.1 default source address instead of the configured physical address.
///
/// Builds parameters only — no bus is opened, so these run anywhere.
/// </summary>
public class ConnectorParametersTests
{
    private static KnxConnectionService CreateService() => new(
        NullLogger<KnxConnectionService>.Instance,
        Mock.Of<IGroupAddressCacheService>(),
        Mock.Of<ITelegramQueue>(),
        Mock.Of<IServiceScopeFactory>());

    private static KnxConfiguration Config(ConnectionType type, string ip, int port = 3671,
        string physicalAddress = "1.0.58") => new()
    {
        ConnectionType = type,
        IpAddress = ip,
        Port = port,
        PhysicalAddress = physicalAddress
    };

    [Fact]
    public async Task Tunneling_BuildsUnicastTunnelToTheGateway()
    {
        var parameters = await CreateService().BuildConnectorParameters(
            Config(ConnectionType.Tunneling, "192.168.10.60", 3671), keyringBlob: null);

        var tunnel = parameters.Should().BeOfType<IpTunnelingConnectorParameters>().Subject;
        tunnel.HostAddress.Should().Be("192.168.10.60");
        tunnel.IpPort.Should().Be(3671);
    }

    [Fact]
    public async Task Routing_JoinsTheConfiguredMulticastGroup()
    {
        var parameters = await CreateService().BuildConnectorParameters(
            Config(ConnectionType.Routing, "224.0.23.12"), keyringBlob: null);

        var routing = parameters.Should().BeOfType<IpRoutingConnectorParameters>().Subject;
        routing.MulticastAddress.Should().Be(IPAddress.Parse("224.0.23.12"));
    }

    [Fact]
    public async Task Routing_UsesTheConfiguredPhysicalAddressAsSource()
    {
        var parameters = await CreateService().BuildConnectorParameters(
            Config(ConnectionType.Routing, "224.0.23.12", physicalAddress: "1.0.58"), keyringBlob: null);

        var routing = (IpRoutingConnectorParameters)parameters;
        routing.KnxAddress.Should().Be(new IndividualAddress("1.0.58"));
        routing.KnxAddress.Should().NotBe(IpRoutingConnectorParameters.DefaultKnxAddress,
            "sending under Falcon's 0.0.1 default would collide with every other defaulting endpoint");
    }

    [Fact]
    public async Task Routing_HonoursANonStandardPort()
    {
        var parameters = await CreateService().BuildConnectorParameters(
            Config(ConnectionType.Routing, "224.0.23.12", port: 3672), keyringBlob: null);

        ((IpRoutingConnectorParameters)parameters).IpPort.Should().Be(3672);
    }

    [Theory]
    [InlineData("not-an-address")]
    [InlineData("")]
    [InlineData("1.0")]
    [InlineData("20.0.58")] // area only has 4 bits, so 20 is out of range
    public async Task Routing_FallsBackToTheDefaultSource_WhenPhysicalAddressIsUnusable(string physicalAddress)
    {
        var parameters = await CreateService().BuildConnectorParameters(
            Config(ConnectionType.Routing, "224.0.23.12", physicalAddress: physicalAddress),
            keyringBlob: null);

        var routing = parameters.Should().BeOfType<IpRoutingConnectorParameters>().Subject;
        routing.KnxAddress.Should().Be(IpRoutingConnectorParameters.DefaultKnxAddress);
    }
}
