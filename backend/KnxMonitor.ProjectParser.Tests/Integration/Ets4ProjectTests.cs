using FluentAssertions;
using KnxMonitor.ProjectParser.Core.Enums;
using KnxMonitor.ProjectParser.Core.Models;
using KnxMonitor.ProjectParser.Tests.Fixtures;

namespace KnxMonitor.ProjectParser.Tests.Integration;

public class Ets4ProjectTests : IClassFixture<SampleFileFixture>
{
    private readonly SampleFileFixture _fixture;

    public Ets4ProjectTests(SampleFileFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task ParseProject_Ets4WithPassword_Success()
    {
        // Arrange
        var samplePath = Path.Combine("TestData", "test_project-ets4.knxproj");
        await using var stream = File.OpenRead(samplePath);
        var options = new ParserOptions { Password = "test" };

        // Act
        var result = await _fixture.Parser.ParseAsync(stream, options);

        // Assert
        result.Should().NotBeNull();
        result.Features.EtsVersion.Should().Be(EtsVersion.Ets4);
        result.Features.HasPassword.Should().BeTrue();

        result.GroupAddresses.Should().NotBeEmpty();
        result.Devices.Should().NotBeEmpty();

        // Validate addresses
        foreach (var ga in result.GroupAddresses)
        {
            ga.Address.Should().NotBeNullOrEmpty();
            ga.Name.Should().NotBeNullOrEmpty();
        }

        foreach (var device in result.Devices)
        {
            device.PhysicalAddress.Should().MatchRegex(@"^\d{1,2}\.\d{1,2}\.\d{1,3}$");
        }
    }

    [Fact]
    public async Task ParseProject_Ets4NoPassword_Success()
    {
        // Arrange
        var samplePath = Path.Combine("TestData", "test_project-ets4-no_password.knxproj");
        await using var stream = File.OpenRead(samplePath);
        var options = new ParserOptions { Password = null };

        // Act
        var result = await _fixture.Parser.ParseAsync(stream, options);

        // Assert
        result.Should().NotBeNull();
        result.Features.EtsVersion.Should().Be(EtsVersion.Ets4);
        result.Features.HasPassword.Should().BeFalse();

        result.GroupAddresses.Should().NotBeEmpty();
        result.Devices.Should().NotBeEmpty();

        // Validate addresses
        foreach (var ga in result.GroupAddresses)
        {
            ga.Address.Should().NotBeNullOrEmpty();
            ga.Name.Should().NotBeNullOrEmpty();
        }
    }

    [Fact]
    public async Task DetectFeatures_Ets4WithPassword_ReturnsCorrectFeatures()
    {
        // Arrange
        var samplePath = Path.Combine("TestData", "test_project-ets4.knxproj");
        await using var stream = File.OpenRead(samplePath);

        // Act
        var features = await _fixture.FeatureDetector.DetectAsync(stream);

        // Assert
        features.Should().NotBeNull();
        features.EtsVersion.Should().Be(EtsVersion.Ets4);
        features.HasPassword.Should().BeTrue();
    }

    [Fact]
    public async Task DetectFeatures_Ets4NoPassword_ReturnsCorrectFeatures()
    {
        // Arrange
        var samplePath = Path.Combine("TestData", "test_project-ets4-no_password.knxproj");
        await using var stream = File.OpenRead(samplePath);

        // Act
        var features = await _fixture.FeatureDetector.DetectAsync(stream);

        // Assert
        features.Should().NotBeNull();
        features.EtsVersion.Should().Be(EtsVersion.Ets4);
        features.HasPassword.Should().BeFalse();
    }

    [Fact]
    public async Task ParseProject_Ets4NoPassword_ResolvesManufacturerName()
    {
        // Arrange
        var samplePath = Path.Combine("TestData", "test_project-ets4-no_password.knxproj");
        await using var stream = File.OpenRead(samplePath);
        var options = new ParserOptions { Password = null };

        // Act
        var result = await _fixture.Parser.ParseAsync(stream, options);

        // Assert: the manufacturer is resolved to its real NAME from knx_master.xml,
        // not the useless "M" the old RefId-split produced.
        result.Devices.Should().NotBeEmpty();
        result.Devices.Should().Contain(d => d.Manufacturer == "ABB");

        foreach (var device in result.Devices)
        {
            device.Manufacturer.Should().NotBeNullOrEmpty();
            device.Manufacturer.Should().NotBe("M");
            device.Manufacturer.Should().NotBe("Unknown");
        }
    }

    [Fact]
    public async Task ParseProject_Ets4Statistics_ArePopulated()
    {
        // Arrange
        var samplePath = Path.Combine("TestData", "test_project-ets4.knxproj");
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
    public async Task ParseProject_Ets4ProgressReporting_WorksCorrectly()
    {
        // Arrange
        var samplePath = Path.Combine("TestData", "test_project-ets4.knxproj");
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
}
