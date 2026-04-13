using FluentAssertions;
using KnxMonitor.ProjectParser.Core.Enums;
using KnxMonitor.ProjectParser.Core.Models;
using KnxMonitor.ProjectParser.Tests.Fixtures;
using Xunit;

namespace KnxMonitor.ProjectParser.Tests.Integration;

// All ETS5 integration cases use proprietary samples that are not committed.
// They run when the files are present locally and are skipped otherwise.
public class Ets5ProjectTests : IClassFixture<SampleFileFixture>
{
    private const string MainSample = "myProject_ets_v5.7.7.knxproj";
    private const string SecureSample = "TestMitSecure_ets_v5.7.7_secure.knxproj";

    private readonly SampleFileFixture _fixture;

    public Ets5ProjectTests(SampleFileFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task ParseProject_MainSample_Success()
    {
        Skip.IfNot(TestSamples.Exists(MainSample), $"missing fixture: {MainSample}");

        await using var stream = File.OpenRead(TestSamples.Path(MainSample));
        var result = await _fixture.Parser.ParseAsync(stream, new ParserOptions());

        result.Features.EtsVersion.Should().Be(EtsVersion.Ets5);
        result.Features.HasPassword.Should().BeFalse();
        result.Features.ProjectId.Should().Be("P-046B");
        result.GroupAddresses.Count.Should().Be(841);
        result.Devices.Count.Should().Be(94);

        foreach (var ga in result.GroupAddresses)
        {
            ga.Address.Should().MatchRegex(@"^\d{1,2}/\d{1,2}/\d{1,3}$");
            ga.Name.Should().NotBeNullOrEmpty();
        }
        foreach (var d in result.Devices)
        {
            d.PhysicalAddress.Should().MatchRegex(@"^\d{1,2}\.\d{1,2}\.\d{1,3}$");
        }
    }

    [SkippableFact]
    public async Task ParseProject_PasswordProtected_Success()
    {
        Skip.IfNot(TestSamples.Exists(SecureSample), $"missing fixture: {SecureSample}");

        await using var stream = File.OpenRead(TestSamples.Path(SecureSample));
        var result = await _fixture.Parser.ParseAsync(stream, new ParserOptions { Password = "affe" });

        result.Features.EtsVersion.Should().Be(EtsVersion.Ets5);
        result.Features.HasPassword.Should().BeTrue();
        result.Features.ProjectId.Should().Be("P-0531");
        result.GroupAddresses.Count.Should().Be(1);
        result.GroupAddresses[0].Address.Should().Be("0/0/1");
        result.GroupAddresses[0].Name.Should().Be("test");
        result.Devices.Count.Should().Be(2);
    }

    [SkippableFact]
    public async Task DetectFeatures_MainSample_ReturnsCorrectFeatures()
    {
        Skip.IfNot(TestSamples.Exists(MainSample), $"missing fixture: {MainSample}");

        await using var stream = File.OpenRead(TestSamples.Path(MainSample));
        var features = await _fixture.FeatureDetector.DetectAsync(stream);

        features.EtsVersion.Should().Be(EtsVersion.Ets5);
        features.HasPassword.Should().BeFalse();
        features.HasKnxSecure.Should().BeFalse();
        features.ProjectId.Should().Be("P-046B");
    }

    [SkippableFact]
    public async Task DetectFeatures_PasswordProtected_ReturnsCorrectFeatures()
    {
        Skip.IfNot(TestSamples.Exists(SecureSample), $"missing fixture: {SecureSample}");

        await using var stream = File.OpenRead(TestSamples.Path(SecureSample));
        var features = await _fixture.FeatureDetector.DetectAsync(stream);

        features.EtsVersion.Should().Be(EtsVersion.Ets5);
        features.HasPassword.Should().BeTrue();
        features.ProjectId.Should().Be("P-0531");
    }

    [SkippableFact]
    public async Task ParseProject_Statistics_ArePopulated()
    {
        Skip.IfNot(TestSamples.Exists(MainSample), $"missing fixture: {MainSample}");

        await using var stream = File.OpenRead(TestSamples.Path(MainSample));
        var result = await _fixture.Parser.ParseAsync(stream, new ParserOptions());

        result.Statistics.Should().NotBeNull();
        result.Statistics.Duration.Should().BePositive();
        result.Statistics.GroupAddressCount.Should().Be(841);
        result.Statistics.DeviceCount.Should().Be(94);
        result.Statistics.Warnings.Should().BeEmpty();
    }

    [SkippableFact]
    public async Task ParseProject_ProgressReporting_WorksCorrectly()
    {
        Skip.IfNot(TestSamples.Exists(MainSample), $"missing fixture: {MainSample}");

        await using var stream = File.OpenRead(TestSamples.Path(MainSample));
        var reports = new List<ParserProgress>();
        var progress = new Progress<ParserProgress>(p => reports.Add(p));

        await _fixture.Parser.ParseAsync(stream, new ParserOptions(), progress);

        reports.Should().Contain(p => p.Step == ParseStep.OpenZip);
        reports.Should().Contain(p => p.Step == ParseStep.DetectFeatures);
        reports.Should().Contain(p => p.Step == ParseStep.ParseGroupAddresses);
        reports.Should().Contain(p => p.Step == ParseStep.ParseDevices);
        reports.Should().Contain(p => p.Step == ParseStep.Complete);
    }
}
