using FluentAssertions;
using KnxMonitor.ProjectParser.Core.Enums;
using KnxMonitor.ProjectParser.Core.Models;
using KnxMonitor.ProjectParser.Services;
using KnxMonitor.ProjectParser.Tests.Fixtures;
using Xunit;

namespace KnxMonitor.ProjectParser.Tests.Integration;

/// <summary>
/// Current-ETS regression cases (GitHub issue #2). The sample was exported with ETS 6.4.1
/// (ToolVersion 6.4.8718.0) and uses project schema 23 — a schema the old hard-coded namespace
/// list did not know. The file is proprietary and not committed, so these skip on a fresh clone.
/// </summary>
public class Ets64ProjectTests : IClassFixture<SampleFileFixture>
{
    private const string Sample = "test_ets_v6.4.1.knxproj";
    private const string PasswordSample = "test_ets_v6.4.1_password.knxproj";
    private const string SamplePassword = "affe";

    private readonly SampleFileFixture _fixture;

    public Ets64ProjectTests(SampleFileFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task DetectFeatures_Ets641_ReturnsEts6()
    {
        Skip.IfNot(TestSamples.Exists(Sample), $"missing fixture: {Sample}");

        await using var stream = File.OpenRead(TestSamples.Path(Sample));
        var features = await _fixture.FeatureDetector.DetectAsync(stream);

        features.EtsVersion.Should().Be(EtsVersion.Ets6);
        features.HasPassword.Should().BeFalse();
        features.ProjectId.Should().Be("P-01C0");
        features.AddressingStyle.Should().Be(AddressingStyle.ThreeLevel);
    }

    [SkippableFact]
    public async Task ParseProject_Ets641_Success()
    {
        Skip.IfNot(TestSamples.Exists(Sample), $"missing fixture: {Sample}");

        await using var stream = File.OpenRead(TestSamples.Path(Sample));
        var result = await _fixture.Parser.ParseAsync(stream, new ParserOptions());

        result.Features.EtsVersion.Should().Be(EtsVersion.Ets6);
        result.GroupAddresses.Should().HaveCount(2);
        result.Devices.Should().HaveCount(3);

        result.GroupAddresses.Select(ga => ga.Address).Should().Contain(new[] { "0/0/1", "0/0/2" });
        result.Devices.Should().OnlyContain(d => !string.IsNullOrEmpty(d.Manufacturer));
    }

    /// <summary>
    /// The reported failure case, with a project ETS itself exported WITH a password: project.xml /
    /// 0.xml are unreadable up front, so the version can only come from knx_master.xml's schema-23
    /// namespace, and the ZIP password is the PBKDF2-derived form of "affe".
    /// Devices must keep product name and manufacturer — those come from the M-XXXX/ folders in the
    /// outer (unencrypted) archive, which used to be dropped for protected projects.
    /// </summary>
    [SkippableFact]
    public async Task ParseProject_Ets641_WithPassword_Success()
    {
        Skip.IfNot(TestSamples.Exists(PasswordSample), $"missing fixture: {PasswordSample}");

        await using var stream = File.OpenRead(TestSamples.Path(PasswordSample));
        var result = await _fixture.Parser.ParseAsync(
            stream, new ParserOptions { Password = SamplePassword });

        result.Features.EtsVersion.Should().Be(EtsVersion.Ets6);
        result.Features.HasPassword.Should().BeTrue();
        result.Features.AddressingStyle.Should().Be(AddressingStyle.ThreeLevel);

        result.GroupAddresses.Select(ga => ga.Address).Should().Contain(new[] { "0/0/1", "0/0/2" });
        result.Devices.Should().HaveCount(3);
        result.Devices.Should().OnlyContain(d => d.Manufacturer == "MDT technologies");
        result.Devices.Should().OnlyContain(d => !string.IsNullOrEmpty(d.ProductName));
    }

    /// <summary>
    /// Same project, once plain and once password-protected: both must yield identical data.
    /// </summary>
    [SkippableFact]
    public async Task ParseProject_Ets641_PlainAndProtected_YieldSameData()
    {
        Skip.IfNot(TestSamples.Exists(Sample), $"missing fixture: {Sample}");
        Skip.IfNot(TestSamples.Exists(PasswordSample), $"missing fixture: {PasswordSample}");

        await using var plainStream = File.OpenRead(TestSamples.Path(Sample));
        var plain = await _fixture.Parser.ParseAsync(plainStream, new ParserOptions());

        await using var protectedStream = File.OpenRead(TestSamples.Path(PasswordSample));
        var protectedResult = await _fixture.Parser.ParseAsync(
            protectedStream, new ParserOptions { Password = SamplePassword });

        protectedResult.GroupAddresses.Select(ga => ga.Address)
            .Should().BeEquivalentTo(plain.GroupAddresses.Select(ga => ga.Address));
        protectedResult.Devices.Select(d => $"{d.PhysicalAddress} {d.Name} {d.Manufacturer}")
            .Should().BeEquivalentTo(plain.Devices.Select(d => $"{d.PhysicalAddress} {d.Name} {d.Manufacturer}"));
    }

    /// <summary>
    /// Same scenario, but the protected archive is re-packed from the plain sample on the fly — keeps
    /// the coverage alive even when only one of the two fixtures is present.
    /// </summary>
    [SkippableFact]
    public async Task ParseProject_Ets641_RepackedWithPassword_Success()
    {
        Skip.IfNot(TestSamples.Exists(Sample), $"missing fixture: {Sample}");

        using var protectedZip = TestZipBuilder.RepackAsPasswordProtected(
            TestSamples.Path(Sample), "affe");

        var result = await _fixture.Parser.ParseAsync(
            protectedZip, new ParserOptions { Password = "affe" });

        result.Features.EtsVersion.Should().Be(EtsVersion.Ets6);
        result.Features.HasPassword.Should().BeTrue();
        result.GroupAddresses.Should().HaveCount(2);
        result.Devices.Should().HaveCount(3);
    }

    /// <summary>
    /// Same again without knx_master.xml — no version marker survives the pre-scan at all, so the
    /// version can only be recovered from the decrypted project.xml.
    /// </summary>
    [SkippableFact]
    public async Task ParseProject_Ets641_PasswordAndNoMasterData_RecoversAfterUnlock()
    {
        Skip.IfNot(TestSamples.Exists(Sample), $"missing fixture: {Sample}");

        using var protectedZip = TestZipBuilder.RepackAsPasswordProtected(
            TestSamples.Path(Sample), "affe", includeMasterData: false);

        var result = await _fixture.Parser.ParseAsync(
            protectedZip, new ParserOptions { Password = "affe" });

        result.Features.EtsVersion.Should().Be(EtsVersion.Ets6);
        result.GroupAddresses.Should().HaveCount(2);
    }

    [SkippableFact]
    public async Task ParseProject_Ets641_WrongPassword_ThrowsWithClearMessage()
    {
        Skip.IfNot(TestSamples.Exists(Sample), $"missing fixture: {Sample}");

        using var protectedZip = TestZipBuilder.RepackAsPasswordProtected(
            TestSamples.Path(Sample), "affe");

        var act = async () => await _fixture.Parser.ParseAsync(
            protectedZip, new ParserOptions { Password = "wrong" });

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*password is wrong*");
    }
}
