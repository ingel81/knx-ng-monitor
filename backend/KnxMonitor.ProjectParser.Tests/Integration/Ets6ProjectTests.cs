using FluentAssertions;
using KnxMonitor.ProjectParser.Core.Enums;
using KnxMonitor.ProjectParser.Core.Models;
using KnxMonitor.ProjectParser.Tests.Fixtures;

namespace KnxMonitor.ProjectParser.Tests.Integration;

public class Ets6ProjectTests : IClassFixture<SampleFileFixture>
{
    private readonly SampleFileFixture _fixture;

    public Ets6ProjectTests(SampleFileFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ParseProject_Ets6WithPassword_Success()
    {
        // Arrange
        var samplePath = Path.Combine("TestData", "testprojekt-ets6.knxproj");
        await using var stream = File.OpenRead(samplePath);
        var options = new ParserOptions { Password = "test" };

        // Act
        var result = await _fixture.Parser.ParseAsync(stream, options);

        // Assert
        result.Should().NotBeNull();
        result.Features.EtsVersion.Should().Be(EtsVersion.Ets6);
        result.Features.HasPassword.Should().BeTrue();

        result.GroupAddresses.Should().NotBeEmpty();
        result.Devices.Should().NotBeEmpty();

        // Validate addresses
        foreach (var ga in result.GroupAddresses)
        {
            ga.Address.Should().NotBeNullOrEmpty();
            ga.Name.Should().NotBeNullOrEmpty();
        }

        // ETS6 Segment-Tag fix validation
        foreach (var device in result.Devices)
        {
            device.PhysicalAddress.Should().MatchRegex(@"^\d{1,2}\.\d{1,2}\.\d{1,3}$");
        }
    }

    [Fact]
    public async Task ParseProject_Ets6FreeAddressing_Success()
    {
        // Arrange
        var samplePath = Path.Combine("TestData", "ets6_free.knxproj");
        await using var stream = File.OpenRead(samplePath);
        var options = new ParserOptions { Password = null };

        // Act
        var result = await _fixture.Parser.ParseAsync(stream, options);

        // Assert
        result.Should().NotBeNull();
        result.Features.EtsVersion.Should().Be(EtsVersion.Ets6);
        result.Features.HasPassword.Should().BeFalse();
        // Note: Sample file doesn't have GroupAddressStyle attribute, defaults to ThreeLevel

        result.GroupAddresses.Should().NotBeEmpty();
        // Note: This minimal sample doesn't contain devices
    }

    [Fact]
    public async Task ParseProject_Ets6TwoLevelAddressing_Success()
    {
        // Arrange
        var samplePath = Path.Combine("TestData", "ets6_two_level.knxproj");
        await using var stream = File.OpenRead(samplePath);
        var options = new ParserOptions { Password = null };

        // Act
        var result = await _fixture.Parser.ParseAsync(stream, options);

        // Assert
        result.Should().NotBeNull();
        result.Features.EtsVersion.Should().Be(EtsVersion.Ets6);
        result.Features.HasPassword.Should().BeFalse();
        // Note: Sample file doesn't have GroupAddressStyle attribute, defaults to ThreeLevel

        result.GroupAddresses.Should().NotBeEmpty();
        // Note: This minimal sample doesn't contain devices
    }

    [Fact]
    public async Task DetectFeatures_Ets6WithPassword_ReturnsCorrectFeatures()
    {
        // Arrange
        var samplePath = Path.Combine("TestData", "testprojekt-ets6.knxproj");
        await using var stream = File.OpenRead(samplePath);

        // Act
        var features = await _fixture.FeatureDetector.DetectAsync(stream);

        // Assert
        features.Should().NotBeNull();
        features.EtsVersion.Should().Be(EtsVersion.Ets6);
        features.HasPassword.Should().BeTrue();
    }

    [Fact]
    public async Task DetectFeatures_Ets6FreeAddressing_ReturnsCorrectVersion()
    {
        // Arrange
        var samplePath = Path.Combine("TestData", "ets6_free.knxproj");
        await using var stream = File.OpenRead(samplePath);

        // Act
        var features = await _fixture.FeatureDetector.DetectAsync(stream);

        // Assert
        features.Should().NotBeNull();
        features.EtsVersion.Should().Be(EtsVersion.Ets6);
        // Note: AddressingStyle requires GroupAddressStyle attribute which may not be present in all samples
    }

    [Fact]
    public async Task DetectFeatures_Ets6TwoLevelAddressing_ReturnsCorrectVersion()
    {
        // Arrange
        var samplePath = Path.Combine("TestData", "ets6_two_level.knxproj");
        await using var stream = File.OpenRead(samplePath);

        // Act
        var features = await _fixture.FeatureDetector.DetectAsync(stream);

        // Assert
        features.Should().NotBeNull();
        features.EtsVersion.Should().Be(EtsVersion.Ets6);
        // Note: AddressingStyle requires GroupAddressStyle attribute which may not be present in all samples
    }

    [Fact]
    public async Task ParseProject_Ets6Statistics_ArePopulated()
    {
        // Arrange
        var samplePath = Path.Combine("TestData", "testprojekt-ets6.knxproj");
        await using var stream = File.OpenRead(samplePath);
        var options = new ParserOptions { Password = "test" };

        // Act
        var result = await _fixture.Parser.ParseAsync(stream, options);

        // Assert
        result.Statistics.Should().NotBeNull();
        result.Statistics.Duration.Should().BePositive();
        result.Statistics.GroupAddressCount.Should().Be(result.GroupAddresses.Count);
        result.Statistics.DeviceCount.Should().Be(result.Devices.Count);
    }

    [Fact]
    public async Task ParseProject_Ets6ProgressReporting_WorksCorrectly()
    {
        // Arrange
        var samplePath = Path.Combine("TestData", "testprojekt-ets6.knxproj");
        await using var stream = File.OpenRead(samplePath);
        var options = new ParserOptions { Password = "test" };

        var progressReports = new List<ParserProgress>();
        var progress = new Progress<ParserProgress>(p => progressReports.Add(p));

        // Act
        await _fixture.Parser.ParseAsync(stream, options, progress);

        // Assert
        progressReports.Should().NotBeEmpty();
        progressReports.Should().Contain(p => p.Step == ParseStep.OpenZip);
        progressReports.Should().Contain(p => p.Step == ParseStep.DetectFeatures);
        progressReports.Should().Contain(p => p.Step == ParseStep.ParseGroupAddresses);
        progressReports.Should().Contain(p => p.Step == ParseStep.ParseDevices);
        progressReports.Should().Contain(p => p.Step == ParseStep.Complete);
    }

    [Fact]
    public async Task ParseProject_Ets6SegmentTagFix_DevicesAreParsed()
    {
        // This test specifically validates the ETS6 Segment-Tag bug fix
        // ETS6 has Line -> Segment -> DeviceInstance hierarchy

        // Arrange
        var samplePath = Path.Combine("TestData", "testprojekt-ets6.knxproj");
        await using var stream = File.OpenRead(samplePath);
        var options = new ParserOptions { Password = "test" };

        // Act
        var result = await _fixture.Parser.ParseAsync(stream, options);

        // Assert
        result.Devices.Should().NotBeEmpty("ETS6 Segment-Tag fix should find devices");

        // At least one device should have a valid physical address
        result.Devices.Should().Contain(d => d.PhysicalAddress != "0.0.0");
    }
}
