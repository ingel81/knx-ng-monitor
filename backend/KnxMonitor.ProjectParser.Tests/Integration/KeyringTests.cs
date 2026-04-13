using FluentAssertions;
using KnxMonitor.ProjectParser.Core.Models;
using KnxMonitor.ProjectParser.Tests.Fixtures;
using Xunit;

namespace KnxMonitor.ProjectParser.Tests.Integration;

// Uses proprietary TestMitSecure samples; skipped if not present locally.
public class KeyringTests : IClassFixture<SampleFileFixture>
{
    private const string KeyringFile = "TestMitSecure_ets_v5.7.7_secure.knxkeys";
    private const string ProjectFile = "TestMitSecure_ets_v5.7.7_secure.knxproj";
    private const string Password = "affe";

    private readonly SampleFileFixture _fixture;

    public KeyringTests(SampleFileFixture fixture)
    {
        _fixture = fixture;
    }

    [SkippableFact]
    public async Task ReadAsync_TestMitSecureKeyring_DecryptsSuccessfully()
    {
        Skip.IfNot(TestSamples.Exists(KeyringFile), $"missing fixture: {KeyringFile}");

        await using var stream = File.OpenRead(TestSamples.Path(KeyringFile));
        var keyring = await _fixture.KeyringReader.ReadAsync(stream, Password);

        keyring.ProjectName.Should().Be("TestMitSecure");
        keyring.Created.Should().Be("2025-10-30T14:27:52");
        keyring.BackboneMulticastAddress.Should().Be("224.0.23.12");
        Convert.ToHexString(keyring.BackboneKey!).ToLowerInvariant()
            .Should().Be("97a62b652cb473fa5a2f16f10e27e4af");

        keyring.Devices.Should().HaveCount(1);
        keyring.Devices[0].IndividualAddress.Should().Be("1.1.0");
        Convert.ToHexString(keyring.Devices[0].ToolKey!).ToLowerInvariant()
            .Should().Be("b91d1106635b6112544ce7a0b7026c28");
    }

    [SkippableFact]
    public async Task ReadAsync_EmptyPassword_Throws()
    {
        Skip.IfNot(TestSamples.Exists(KeyringFile), $"missing fixture: {KeyringFile}");

        await using var stream = File.OpenRead(TestSamples.Path(KeyringFile));
        var act = async () => await _fixture.KeyringReader.ReadAsync(stream, string.Empty);

        await act.Should().ThrowAsync<ArgumentException>();
    }

    [SkippableFact]
    public async Task ParseAsync_WithKeyring_IncludesKeyringData()
    {
        Skip.IfNot(TestSamples.Exists(ProjectFile) && TestSamples.Exists(KeyringFile),
            $"missing fixtures: {ProjectFile} / {KeyringFile}");

        await using var projectStream = File.OpenRead(TestSamples.Path(ProjectFile));
        await using var keyringStream = File.OpenRead(TestSamples.Path(KeyringFile));

        var options = new ParserOptions
        {
            Password = Password,
            KeyringStream = keyringStream,
            KeyringPassword = Password
        };

        var result = await _fixture.Parser.ParseAsync(projectStream, options);

        result.Keyring.Should().NotBeNull();
        result.Keyring!.ProjectName.Should().Be("TestMitSecure");
        result.Keyring.Devices.Should().HaveCount(1);
        result.Keyring.Devices[0].ToolKey.Should().NotBeNull();
    }

    [SkippableFact]
    public async Task ParseAsync_KeyringStreamWithoutPassword_Throws()
    {
        Skip.IfNot(TestSamples.Exists(ProjectFile) && TestSamples.Exists(KeyringFile),
            $"missing fixtures: {ProjectFile} / {KeyringFile}");

        await using var projectStream = File.OpenRead(TestSamples.Path(ProjectFile));
        await using var keyringStream = File.OpenRead(TestSamples.Path(KeyringFile));

        var options = new ParserOptions
        {
            Password = Password,
            KeyringStream = keyringStream,
            KeyringPassword = null
        };

        var act = async () => await _fixture.Parser.ParseAsync(projectStream, options);

        await act.Should().ThrowAsync<InvalidOperationException>();
    }
}
