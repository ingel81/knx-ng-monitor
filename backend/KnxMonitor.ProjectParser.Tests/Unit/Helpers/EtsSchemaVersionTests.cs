using FluentAssertions;
using KnxMonitor.ProjectParser.Core.Enums;
using KnxMonitor.ProjectParser.Helpers;

namespace KnxMonitor.ProjectParser.Tests.Unit.Helpers;

public class EtsSchemaVersionTests
{
    [Theory]
    [InlineData("http://knx.org/xml/project/11", 11)]
    [InlineData("http://knx.org/xml/project/20", 20)]
    [InlineData("http://knx.org/xml/project/23", 23)]
    [InlineData("https://knx.org/xml/project/23", 23)]
    [InlineData("http://knx.org/xml/project/23/", 23)]
    public void ParseSchemaVersion_ExtractsNumber(string ns, int expected)
    {
        EtsSchemaVersion.ParseSchemaVersion(ns).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("http://knx.org/xml/project/")]
    [InlineData("http://knx.org/xml/project/abc")]
    [InlineData("http://www.w3.org/2001/XMLSchema")]
    public void ParseSchemaVersion_NonProjectNamespace_ReturnsNull(string? ns)
    {
        EtsSchemaVersion.ParseSchemaVersion(ns).Should().BeNull();
    }

    [Theory]
    [InlineData(10, EtsVersion.Unknown)]
    [InlineData(11, EtsVersion.Ets4)]
    [InlineData(14, EtsVersion.Ets4)]
    [InlineData(20, EtsVersion.Ets5)]
    [InlineData(21, EtsVersion.Ets6)]
    [InlineData(22, EtsVersion.Ets6)]
    [InlineData(23, EtsVersion.Ets6)]
    [InlineData(30, EtsVersion.Ets6)]
    public void FromSchemaVersion_MapsRanges(int schema, EtsVersion expected)
    {
        EtsSchemaVersion.FromSchemaVersion(schema).Should().Be(expected);
    }

    [Theory]
    [InlineData("4.1.0", EtsVersion.Ets4)]
    [InlineData("5.7.7", EtsVersion.Ets5)]
    [InlineData("6.2.7302.0", EtsVersion.Ets6)]
    [InlineData("9.9", EtsVersion.Unknown)]
    [InlineData(null, EtsVersion.Unknown)]
    public void FromToolVersion_MapsMajor(string? toolVersion, EtsVersion expected)
    {
        EtsSchemaVersion.FromToolVersion(toolVersion).Should().Be(expected);
    }

    [Theory]
    [InlineData("ETS4", EtsVersion.Ets4)]
    [InlineData("ETS5", EtsVersion.Ets5)]
    [InlineData("ETS6", EtsVersion.Ets6)]
    [InlineData("SomeOtherTool", EtsVersion.Unknown)]
    [InlineData(null, EtsVersion.Unknown)]
    public void FromCreatedBy_MapsTool(string? createdBy, EtsVersion expected)
    {
        EtsSchemaVersion.FromCreatedBy(createdBy).Should().Be(expected);
    }
}
