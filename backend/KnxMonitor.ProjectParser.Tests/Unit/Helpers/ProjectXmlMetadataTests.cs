using System.Xml.Linq;
using FluentAssertions;
using KnxMonitor.ProjectParser.Core.Enums;
using KnxMonitor.ProjectParser.Helpers;

namespace KnxMonitor.ProjectParser.Tests.Unit.Helpers;

public class ProjectXmlMetadataTests
{
    private static XElement? Root(string xml) => XDocument.Parse(xml).Root;

    [Fact]
    public void ReadEtsVersion_PrefersNamespaceSchema()
    {
        var root = Root("<KNX xmlns=\"http://knx.org/xml/project/23\" ToolVersion=\"6.2.7302.0\"/>");

        ProjectXmlMetadata.ReadEtsVersion(root).Should().Be(EtsVersion.Ets6);
    }

    [Fact]
    public void ReadEtsVersion_FallsBackToToolVersionThenCreatedBy()
    {
        ProjectXmlMetadata.ReadEtsVersion(Root("<KNX ToolVersion=\"5.7.7\"/>"))
            .Should().Be(EtsVersion.Ets5);

        ProjectXmlMetadata.ReadEtsVersion(Root("<KNX ToolVersion=\"ETS 4.2.0 (Build 3884)\" CreatedBy=\"ETS4\"/>"))
            .Should().Be(EtsVersion.Ets4);
    }

    [Fact]
    public void ReadEtsVersion_NoMarkers_ReturnsUnknown()
    {
        ProjectXmlMetadata.ReadEtsVersion(Root("<KNX/>")).Should().Be(EtsVersion.Unknown);
        ProjectXmlMetadata.ReadEtsVersion(null).Should().Be(EtsVersion.Unknown);
    }

    /// <summary>
    /// The real ETS layout: GroupAddressStyle lives on ProjectInformation, NOT on the document root.
    /// </summary>
    [Theory]
    [InlineData("Free", AddressingStyle.Free)]
    [InlineData("TwoLevel", AddressingStyle.TwoLevel)]
    [InlineData("ThreeLevel", AddressingStyle.ThreeLevel)]
    public void ReadAddressingStyle_FromProjectInformation(string raw, AddressingStyle expected)
    {
        var root = Root(
            "<KNX xmlns=\"http://knx.org/xml/project/23\">" +
            "  <Project Id=\"P-0001\">" +
            $"    <ProjectInformation Name=\"x\" GroupAddressStyle=\"{raw}\" />" +
            "  </Project>" +
            "</KNX>");

        ProjectXmlMetadata.ReadAddressingStyle(root).Should().Be(expected);
    }

    [Fact]
    public void ReadAddressingStyle_FromRootAttribute()
    {
        ProjectXmlMetadata.ReadAddressingStyle(Root("<Project GroupAddressStyle=\"TwoLevel\"/>"))
            .Should().Be(AddressingStyle.TwoLevel);
    }

    [Fact]
    public void ReadAddressingStyle_Missing_ReturnsNull()
    {
        ProjectXmlMetadata.ReadAddressingStyle(Root("<KNX><Project/></KNX>")).Should().BeNull();
        ProjectXmlMetadata.ReadAddressingStyle(null).Should().BeNull();
    }

    [Theory]
    [InlineData("3Level", AddressingStyle.ThreeLevel)]
    [InlineData("2level", AddressingStyle.TwoLevel)]
    [InlineData("freelevel", AddressingStyle.Free)]
    [InlineData("Bogus", AddressingStyle.ThreeLevel)]
    public void ParseAddressingStyle_MapsKnownAndUnknownValues(string raw, AddressingStyle expected)
    {
        ProjectXmlMetadata.ParseAddressingStyle(raw).Should().Be(expected);
    }

    [Fact]
    public void ParseAddressingStyle_Empty_ReturnsNull()
    {
        ProjectXmlMetadata.ParseAddressingStyle("").Should().BeNull();
        ProjectXmlMetadata.ParseAddressingStyle(null).Should().BeNull();
    }
}
