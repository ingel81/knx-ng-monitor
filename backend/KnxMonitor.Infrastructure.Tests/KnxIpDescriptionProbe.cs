using System.Net;
using System.Net.Sockets;
using System.Text;
using Xunit;
using Xunit.Abstractions;

namespace KnxMonitor.Infrastructure.Tests;

/// <summary>
/// MANUAL diagnostic — opt-in via <c>KNX_DESCRIBE_IP</c> (the KNXnet/IP device to ask).
///
/// Sends a <b>unicast</b> KNXnet/IP DESCRIPTION_REQUEST and decodes the Device Info DIB. Unicast
/// is routed between subnets/VLANs, so this reaches a device that multicast discovery cannot —
/// which is exactly the situation when the KNX IP router sits in a different VLAN than the
/// machine running the monitor.
///
/// Answers three things that matter for the routing setup:
///   - is the device reachable and speaking KNXnet/IP at all
///   - which routing multicast group is it configured for (may differ from 224.0.23.12)
///   - does it announce the Routing service family
///
/// Read-only: a description request is a management query, no group telegram is written.
///
/// <code>
/// KNX_DESCRIBE_IP=192.168.10.60 dotnet test KnxMonitor.Infrastructure.Tests \
///   --filter FullyQualifiedName~KnxIpDescriptionProbe --logger "console;verbosity=detailed"
/// </code>
/// </summary>
public class KnxIpDescriptionProbe
{
    private readonly ITestOutputHelper _output;
    public KnxIpDescriptionProbe(ITestOutputHelper output) => _output = output;

    private static readonly Dictionary<byte, string> ServiceFamilies = new()
    {
        [0x02] = "Core", [0x03] = "Device Management", [0x04] = "Tunneling",
        [0x05] = "Routing", [0x06] = "Remote Logging", [0x07] = "Remote Config",
        [0x08] = "Object Server", [0x09] = "Security"
    };

    [Fact]
    public async Task Description_ReportsRouterCapabilitiesOverUnicast()
    {
        var target = Environment.GetEnvironmentVariable("KNX_DESCRIBE_IP");
        if (string.IsNullOrWhiteSpace(target))
        {
            _output.WriteLine("SKIP: set KNX_DESCRIBE_IP=<device ip> to run this against real hardware.");
            return;
        }

        var port = int.TryParse(Environment.GetEnvironmentVariable("KNX_DESCRIBE_PORT"), out var p) ? p : 3671;
        var endpoint = new IPEndPoint(IPAddress.Parse(target), port);

        using var udp = new UdpClient(new IPEndPoint(IPAddress.Any, 0));
        var local = (IPEndPoint)udp.Client.LocalEndPoint!;

        // Learn which local address actually routes to the target, so the HPAI tells the device
        // where to answer. Binding to Any would otherwise put 0.0.0.0 in the frame.
        using var probe = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
        probe.Connect(endpoint);
        var localIp = ((IPEndPoint)probe.LocalEndPoint!).Address;

        // DESCRIPTION_REQUEST (0x0203): header(6) + HPAI(8) with our reply endpoint.
        var frame = new byte[14];
        frame[0] = 0x06; frame[1] = 0x10; frame[2] = 0x02; frame[3] = 0x03;
        frame[4] = 0x00; frame[5] = 0x0E;
        frame[6] = 0x08; frame[7] = 0x01;
        localIp.GetAddressBytes().CopyTo(frame, 8);
        frame[12] = (byte)(local.Port >> 8); frame[13] = (byte)(local.Port & 0xFF);

        _output.WriteLine($"DESCRIPTION_REQUEST {localIp}:{local.Port} -> {endpoint}");
        await udp.SendAsync(frame, frame.Length, endpoint);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        UdpReceiveResult reply;
        try
        {
            reply = await udp.ReceiveAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            Assert.Fail($"no DESCRIPTION_RESPONSE from {endpoint} within 5s - device unreachable, "
                      + "not a KNXnet/IP device, or the reply was dropped by a firewall");
            return;
        }

        var d = reply.Buffer;
        _output.WriteLine($"reply from {reply.RemoteEndPoint}, {d.Length} bytes, "
                        + $"service=0x{d[2]:X2}{d[3]:X2}");

        Assert.True(d.Length >= 60, "DESCRIPTION_RESPONSE too short to contain a Device Info DIB");

        // Device Info DIB starts right after the 6-byte header (no HPAI in a description response).
        var individualAddress = $"{(d[10] >> 4) & 0x0F}.{d[10] & 0x0F}.{d[11]}";
        var multicast = $"{d[20]}.{d[21]}.{d[22]}.{d[23]}";
        var mac = string.Join(":", d.Skip(24).Take(6).Select(b => b.ToString("X2")));
        var name = Encoding.UTF8.GetString(d, 30, 30).TrimEnd('\0', ' ');

        _output.WriteLine($"  FriendlyName      : {name}");
        _output.WriteLine($"  IndividualAddress : {individualAddress}");
        _output.WriteLine($"  RoutingMulticast  : {multicast}   <- use this in Settings for routing");
        _output.WriteLine($"  MacAddress        : {mac}");

        // Supported Service Families DIB follows the 54-byte Device Info DIB.
        var offset = 6 + d[6];
        if (offset + 1 < d.Length && d[offset + 1] == 0x02)
        {
            var length = d[offset];
            var families = new List<string>();
            for (var i = offset + 2; i + 1 < offset + length && i + 1 < d.Length; i += 2)
            {
                var known = ServiceFamilies.TryGetValue(d[i], out var label) ? label : $"0x{d[i]:X2}";
                families.Add($"{known} v{d[i + 1]}");
            }
            _output.WriteLine($"  ServiceFamilies   : {string.Join(", ", families)}");
        }
    }
}
