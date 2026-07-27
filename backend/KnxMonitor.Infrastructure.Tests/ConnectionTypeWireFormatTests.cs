using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using KnxMonitor.Core.Entities;
using KnxMonitor.Core.Enums;
using Xunit;

namespace KnxMonitor.Infrastructure.Tests;

/// <summary>
/// Pins the JSON wire format of <see cref="ConnectionType"/>. The settings UI reads the mode back
/// from GET /api/knx/configurations and compares it against the literal strings "Tunneling" /
/// "Routing"; if the API ever started emitting camelCased or numeric values, the frontend would
/// silently fall back to tunneling and the routing option would look like it does not stick.
///
/// The options here mirror Program.cs — <c>AddControllers().AddJsonOptions(o =&gt;
/// o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()))</c> on top of the
/// ASP.NET Core web defaults (camelCase property names).
/// </summary>
public class ConnectionTypeWireFormatTests
{
    private static JsonSerializerOptions ApiOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    [Theory]
    [InlineData(ConnectionType.Tunneling, "Tunneling")]
    [InlineData(ConnectionType.Routing, "Routing")]
    public void Serialize_EmitsEnumName_NotOrdinalAndNotCamelCased(ConnectionType type, string expected)
    {
        var json = JsonSerializer.Serialize(
            new KnxConfiguration { ConnectionType = type }, ApiOptions());

        json.Should().Contain($"\"connectionType\":\"{expected}\"");
    }

    [Theory]
    [InlineData("\"Routing\"", ConnectionType.Routing)]
    [InlineData("\"Tunneling\"", ConnectionType.Tunneling)]
    // Ordinals stay accepted so a cached older frontend bundle keeps working.
    [InlineData("1", ConnectionType.Routing)]
    [InlineData("0", ConnectionType.Tunneling)]
    public void Deserialize_AcceptsEnumNameAndLegacyOrdinal(string raw, ConnectionType expected)
    {
        var json = $"{{\"ipAddress\":\"224.0.23.12\",\"port\":3671,"
                 + $"\"physicalAddress\":\"1.0.58\",\"connectionType\":{raw}}}";

        var config = JsonSerializer.Deserialize<KnxConfiguration>(json, ApiOptions());

        config!.ConnectionType.Should().Be(expected);
    }

    [Fact]
    public void Default_IsTunneling()
    {
        new KnxConfiguration().ConnectionType.Should().Be(ConnectionType.Tunneling);
    }
}
