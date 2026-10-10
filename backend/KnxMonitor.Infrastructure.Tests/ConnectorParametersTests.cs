using System.Net;
using FluentAssertions;
using Knx.Falcon;
using Knx.Falcon.Configuration;
using IpProtocol = Knx.Falcon.KnxnetIp.IpProtocol;
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
        Mock.Of<IProjectCacheService>(),
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
    public async Task Tunneling_KeepsTheTunnelAcrossShortOutages()
    {
        // #22: without AutoReconnect Falcon drops the tunnel on the first lost heartbeat.
        var parameters = await CreateService().BuildConnectorParameters(
            Config(ConnectionType.Tunneling, "192.168.10.60"), keyringBlob: null);

        parameters.AutoReconnect.Should().BeTrue();
    }

    [Theory]
    [InlineData(TunnelProtocol.Udp, IpProtocol.Udp)]
    [InlineData(TunnelProtocol.Tcp, IpProtocol.Tcp)]
    public async Task Tunneling_UsesTheConfiguredProtocol(TunnelProtocol configured, IpProtocol expected)
    {
        var config = Config(ConnectionType.Tunneling, "192.168.10.60");
        config.TunnelProtocol = configured;

        var tunnel = (IpTunnelingConnectorParameters)await CreateService().BuildConnectorParameters(config, keyringBlob: null);

        tunnel.ProtocolType.Should().Be(expected);
    }

    [Fact]
    public async Task Tunneling_AcceptsAHostName()
    {
        // #23: Falcon resolves the name itself, the configuration only has to pass it through.
        var tunnel = (IpTunnelingConnectorParameters)await CreateService().BuildConnectorParameters(
            Config(ConnectionType.Tunneling, "knx-gateway.local"), keyringBlob: null);

        tunnel.HostAddress.Should().Be("knx-gateway.local");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Tunneling_RequestsTheConfiguredTunnelAddress(bool fallback)
    {
        // #10: reclaiming the same slot instead of occupying a new one on every reconnect.
        var config = Config(ConnectionType.Tunneling, "192.168.10.60");
        config.TunnelAddress = " 1.1.251 ";
        config.TunnelAddressFallback = fallback;

        var tunnel = (IpTunnelingConnectorParameters)await CreateService().BuildConnectorParameters(config, keyringBlob: null);

        tunnel.IndividualAddress.Should().Be(new IndividualAddress("1.1.251"));
        tunnel.FallbackToAnyIndividualAddress.Should().Be(fallback);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not-an-address")]
    public async Task Tunneling_LetsTheInterfaceChoose_WithoutAUsableTunnelAddress(string? tunnelAddress)
    {
        var config = Config(ConnectionType.Tunneling, "192.168.10.60");
        config.TunnelAddress = tunnelAddress;

        var tunnel = (IpTunnelingConnectorParameters)await CreateService().BuildConnectorParameters(config, keyringBlob: null);

        tunnel.IndividualAddress.Should().BeNull();
    }

    [Theory]
    [InlineData("Could not connect to IP interface (The IA requested in the Extended CRI is not a Tunnelling IA).", true)]
    [InlineData("No reply from interface 192.168.99.99:3671", false)]
    [InlineData("Host \"knx-gw.invalid\" not found", false)]
    public void OnlyARejectedTunnelAddressTriggersTheFallback(string message, bool expected)
    {
        // An unreachable interface must not cost a second, equally hopeless connect attempt.
        KnxConnectionService.IsTunnelAddressRejection(new InvalidOperationException(message)).Should().Be(expected);
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
