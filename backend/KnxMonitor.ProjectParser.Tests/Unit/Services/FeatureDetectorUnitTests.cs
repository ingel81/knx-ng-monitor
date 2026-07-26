using FluentAssertions;
using KnxMonitor.ProjectParser.Core.Enums;
using KnxMonitor.ProjectParser.Services;
using KnxMonitor.ProjectParser.Tests.Fixtures;
using Microsoft.Extensions.Logging.Abstractions;

namespace KnxMonitor.ProjectParser.Tests.Unit.Services;

public class FeatureDetectorUnitTests
{
    private static FeatureDetector Detector() =>
        new(NullLogger<FeatureDetector>.Instance);

    [Theory]
    [InlineData("http://knx.org/xml/project/11", EtsVersion.Ets4)]
    [InlineData("http://knx.org/xml/project/12", EtsVersion.Ets4)]
    [InlineData("http://knx.org/xml/project/14", EtsVersion.Ets4)]
    [InlineData("http://knx.org/xml/project/20", EtsVersion.Ets5)]
    [InlineData("http://knx.org/xml/project/21", EtsVersion.Ets6)]  // ETS 6.0
    [InlineData("http://knx.org/xml/project/22", EtsVersion.Ets6)]  // ETS 6.1
    [InlineData("http://knx.org/xml/project/23", EtsVersion.Ets6)]  // ETS 6.2 / 6.3
    [InlineData("http://knx.org/xml/project/24", EtsVersion.Ets6)]  // future schema -> newest loader
    [InlineData("http://knx.org/xml/project/99", EtsVersion.Ets6)]
    [InlineData("http://example.com/not-knx", EtsVersion.Unknown)]
    public async Task DetectAsync_MasterNamespace_MapsVersion(string ns, EtsVersion expected)
    {
        using var zip = TestZipBuilder.BuildOuter(new[]
        {
            ("P-0001.signature", ""),
            ("knx_master.xml", $"<?xml version=\"1.0\"?>\n<KNX xmlns=\"{ns}\"></KNX>"),
            ("P-0001/project.xml", "<Project GroupAddressStyle=\"ThreeLevel\"></Project>"),
            ("P-0001/0.xml", "<KNX/>"),
        });

        var features = await Detector().DetectAsync(zip);

        features.EtsVersion.Should().Be(expected);
        features.ProjectId.Should().Be("P-0001");
        features.HasPassword.Should().BeFalse();
    }

    [Theory]
    [InlineData("4.1", EtsVersion.Ets4)]
    [InlineData("5.7.7", EtsVersion.Ets5)]
    [InlineData("6.0.0", EtsVersion.Ets6)]
    [InlineData("9.9", EtsVersion.Unknown)]
    public async Task DetectAsync_ToolVersionFallback_MapsVersion(string toolVersion, EtsVersion expected)
    {
        using var zip = TestZipBuilder.BuildOuter(new[]
        {
            ("P-0001.signature", ""),
            ("project.xml", $"<Project ToolVersion=\"{toolVersion}\"></Project>"),
        });

        var features = await Detector().DetectAsync(zip);
        features.EtsVersion.Should().Be(expected);
    }

    [Theory]
    [InlineData("ETS4", EtsVersion.Ets4)]
    [InlineData("ETS5", EtsVersion.Ets5)]
    [InlineData("ETS6", EtsVersion.Ets6)]
    public async Task DetectAsync_CreatedByFallback_MapsVersion(string createdBy, EtsVersion expected)
    {
        using var zip = TestZipBuilder.BuildOuter(new[]
        {
            ("P-0001.signature", ""),
            ("P-0001/project.xml", $"<KNX CreatedBy=\"{createdBy}\"></KNX>"),
        });

        var features = await Detector().DetectAsync(zip);
        features.EtsVersion.Should().Be(expected);
    }

    /// <summary>
    /// Regression for GitHub issue #2: a password-protected project from a current ETS release.
    /// project.xml / 0.xml sit inside the encrypted P-XXXX.zip, so knx_master.xml with its newer
    /// schema (22 = ETS 6.1, 23 = ETS 6.2/6.3) is the only readable version marker. The old
    /// hard-coded namespace list only knew schema 21 and returned Unknown -> "No loader available".
    /// </summary>
    [Theory]
    [InlineData("http://knx.org/xml/project/22")]
    [InlineData("http://knx.org/xml/project/23")]
    public async Task DetectAsync_PasswordProtected_NewEtsSchema_DetectsEts6(string ns)
    {
        using var zip = TestZipBuilder.BuildOuter(new[]
        {
            ("P-0001.signature", ""),
            ("P-0001.zip", "encrypted-payload"),
            ("knx_master.xml", $"<?xml version=\"1.0\"?>\n<KNX xmlns=\"{ns}\"></KNX>"),
        });

        var features = await Detector().DetectAsync(zip);

        features.HasPassword.Should().BeTrue();
        features.EtsVersion.Should().Be(EtsVersion.Ets6);
    }

    [Fact]
    public async Task DetectAsync_NoProjectFiles_ReturnsUnknown()
    {
        using var zip = TestZipBuilder.BuildOuter(new[] { ("README.txt", "x") });

        var features = await Detector().DetectAsync(zip);

        features.EtsVersion.Should().Be(EtsVersion.Unknown);
    }

    [Fact]
    public async Task DetectAsync_PasswordProtected_Flagged()
    {
        using var zip = TestZipBuilder.BuildOuter(new[]
        {
            ("P-0001.signature", ""),
            ("P-0001.zip", "dummy"),
            ("knx_master.xml", "<KNX xmlns=\"http://knx.org/xml/project/20\"></KNX>"),
        });

        var features = await Detector().DetectAsync(zip);
        features.HasPassword.Should().BeTrue();
    }

    [Theory]
    [InlineData("ThreeLevel", AddressingStyle.ThreeLevel)]
    [InlineData("TwoLevel", AddressingStyle.TwoLevel)]
    [InlineData("Free", AddressingStyle.Free)]
    [InlineData("FreeLevel", AddressingStyle.Free)]
    [InlineData("3Level", AddressingStyle.ThreeLevel)]
    [InlineData("2Level", AddressingStyle.TwoLevel)]
    [InlineData("Bogus", AddressingStyle.ThreeLevel)]
    public async Task DetectAsync_AddressingStyle_Mapped(string raw, AddressingStyle expected)
    {
        using var zip = TestZipBuilder.BuildOuter(new[]
        {
            ("P-0001.signature", ""),
            ("knx_master.xml", "<KNX xmlns=\"http://knx.org/xml/project/20\"></KNX>"),
            ("P-0001/project.xml", $"<Project GroupAddressStyle=\"{raw}\"></Project>"),
            ("P-0001/0.xml", "<KNX/>"),
        });

        var features = await Detector().DetectAsync(zip);
        features.AddressingStyle.Should().Be(expected);
    }

    /// <summary>
    /// Real ETS files put GroupAddressStyle on &lt;ProjectInformation&gt;, not on the root element.
    /// Reading only the root made every ETS5/6 project look like ThreeLevel, so TwoLevel/Free
    /// projects got three-part group addresses ("0/0/1" instead of "0/1").
    /// </summary>
    [Theory]
    [InlineData("Free", AddressingStyle.Free)]
    [InlineData("TwoLevel", AddressingStyle.TwoLevel)]
    [InlineData("ThreeLevel", AddressingStyle.ThreeLevel)]
    public async Task DetectAsync_AddressingStyle_OnProjectInformation_Mapped(string raw, AddressingStyle expected)
    {
        using var zip = TestZipBuilder.BuildOuter(new[]
        {
            ("P-0001.signature", ""),
            ("knx_master.xml", "<KNX xmlns=\"http://knx.org/xml/project/23\"></KNX>"),
            ("P-0001/project.xml",
                "<KNX xmlns=\"http://knx.org/xml/project/23\"><Project Id=\"P-0001\">"
                + $"<ProjectInformation Name=\"x\" GroupAddressStyle=\"{raw}\" /></Project></KNX>"),
            ("P-0001/0.xml", "<KNX/>"),
        });

        var features = await Detector().DetectAsync(zip);
        features.AddressingStyle.Should().Be(expected);
    }

    /// <summary>ETS4 writes "P-XXXX/Project.xml" with a capital P; entry lookup must be case-insensitive.</summary>
    [Fact]
    public async Task DetectAsync_AddressingStyle_Ets4CapitalisedProjectXml_Mapped()
    {
        using var zip = TestZipBuilder.BuildOuter(new[]
        {
            ("P-0600.signature", ""),
            ("knx_master.xml", "<KNX xmlns=\"http://knx.org/xml/project/11\"></KNX>"),
            ("P-0600/Project.xml",
                "<KNX xmlns=\"http://knx.org/xml/project/11\"><Project Id=\"P-0600\">"
                + "<ProjectInformation Name=\"x\" GroupAddressStyle=\"TwoLevel\" /></Project></KNX>"),
        });

        var features = await Detector().DetectAsync(zip);
        features.AddressingStyle.Should().Be(AddressingStyle.TwoLevel);
    }

    [Fact]
    public async Task DetectAsync_AddressingStyle_EmptyAttribute_DefaultsThreeLevel()
    {
        using var zip = TestZipBuilder.BuildOuter(new[]
        {
            ("P-0001.signature", ""),
            ("knx_master.xml", "<KNX xmlns=\"http://knx.org/xml/project/20\"></KNX>"),
            ("P-0001/project.xml", "<Project></Project>"),
            ("P-0001/0.xml", "<KNX/>"),
        });

        var features = await Detector().DetectAsync(zip);
        features.AddressingStyle.Should().Be(AddressingStyle.ThreeLevel);
    }

    [Fact]
    public async Task DetectAsync_AddressingStyle_ProjectXmlMissing_DefaultsThreeLevel()
    {
        using var zip = TestZipBuilder.BuildOuter(new[]
        {
            ("P-0001.signature", ""),
            ("knx_master.xml", "<KNX xmlns=\"http://knx.org/xml/project/20\"></KNX>"),
            ("P-0001/0.xml", "<KNX/>"),
        });

        var features = await Detector().DetectAsync(zip);
        features.AddressingStyle.Should().Be(AddressingStyle.ThreeLevel);
    }

    [Fact]
    public async Task DetectAsync_AddressingStyle_BrokenXml_FallsBackToThreeLevel()
    {
        using var zip = TestZipBuilder.BuildOuter(new[]
        {
            ("P-0001.signature", ""),
            ("knx_master.xml", "<KNX xmlns=\"http://knx.org/xml/project/20\"></KNX>"),
            ("P-0001/project.xml", "<not-valid-xml"),
            ("P-0001/0.xml", "<KNX/>"),
        });

        var features = await Detector().DetectAsync(zip);
        features.AddressingStyle.Should().Be(AddressingStyle.ThreeLevel);
    }

    [Fact]
    public async Task DetectAsync_KnxSecure_GaDataSecurity_Detected()
    {
        using var zip = TestZipBuilder.BuildOuter(new[]
        {
            ("P-0001.signature", ""),
            ("knx_master.xml", "<KNX xmlns=\"http://knx.org/xml/project/20\"></KNX>"),
            ("P-0001/project.xml", "<Project GroupAddressStyle=\"ThreeLevel\"></Project>"),
            ("P-0001/0.xml", "<KNX xmlns=\"http://knx.org/xml/project/20\"><GroupAddress DataSecurity=\"1\" /></KNX>"),
        });

        var features = await Detector().DetectAsync(zip);
        features.HasKnxSecure.Should().BeTrue();
    }

    [Fact]
    public async Task DetectAsync_KnxSecure_SecureCoupler_Detected()
    {
        using var zip = TestZipBuilder.BuildOuter(new[]
        {
            ("P-0001.signature", ""),
            ("knx_master.xml", "<KNX xmlns=\"http://knx.org/xml/project/20\"></KNX>"),
            ("P-0001/project.xml", "<Project GroupAddressStyle=\"ThreeLevel\"></Project>"),
            ("P-0001/0.xml", "<KNX xmlns=\"http://knx.org/xml/project/20\"><Line><DeviceInstance IsCoupler=\"true\" /><Security /></Line></KNX>"),
        });

        var features = await Detector().DetectAsync(zip);
        features.HasKnxSecure.Should().BeTrue();
    }

    [Fact]
    public async Task DetectAsync_KnxSecure_NoProjectFile_ReturnsFalse()
    {
        using var zip = TestZipBuilder.BuildOuter(new[]
        {
            ("P-0001.signature", ""),
            ("knx_master.xml", "<KNX xmlns=\"http://knx.org/xml/project/20\"></KNX>"),
        });

        var features = await Detector().DetectAsync(zip);
        features.HasKnxSecure.Should().BeFalse();
    }

    [Fact]
    public async Task DetectAsync_KnxSecure_BrokenXml_FallsBackToFalse()
    {
        using var zip = TestZipBuilder.BuildOuter(new[]
        {
            ("P-0001.signature", ""),
            ("knx_master.xml", "<KNX xmlns=\"http://knx.org/xml/project/20\"></KNX>"),
            ("P-0001/project.xml", "<Project GroupAddressStyle=\"ThreeLevel\"></Project>"),
            ("P-0001/0.xml", "<broken-xml"),
        });

        var features = await Detector().DetectAsync(zip);
        features.HasKnxSecure.Should().BeFalse();
    }

    [Fact]
    public async Task DetectAsync_NoZipStream_Throws()
    {
        using var garbage = new MemoryStream(new byte[] { 0x00, 0x01, 0x02 });

        var act = async () => await Detector().DetectAsync(garbage);

        await act.Should().ThrowAsync<Exception>();
    }
}
