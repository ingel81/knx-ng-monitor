using FluentAssertions;
using KnxMonitor.ProjectParser.Core.Enums;
using KnxMonitor.ProjectParser.Core.Models;
using KnxMonitor.ProjectParser.Services;
using KnxMonitor.ProjectParser.Tests.Fixtures;

namespace KnxMonitor.ProjectParser.Tests.Unit.Services;

public class ZipHandlerUnitTests
{
    [Fact]
    public async Task LoadAsync_NoPassword_ReturnsAllFiles()
    {
        using var zip = TestZipBuilder.BuildOuter(new[]
        {
            ("P-0001/0.xml", "<project/>"),
            ("P-0001/dir/",   ""),
            ("knx_master.xml", "<master/>"),
        });

        var features = new ProjectFeatures { HasPassword = false, EtsVersion = EtsVersion.Ets5 };

        var files = await ZipHandler.LoadAsync(zip, features, password: null);

        files.Contains("P-0001/0.xml").Should().BeTrue();
        files.Contains("knx_master.xml").Should().BeTrue();
    }

    [Fact]
    public async Task LoadAsync_PasswordRequired_ButMissing_Throws()
    {
        using var zip = TestZipBuilder.BuildOuter(new[] { ("P-0001.zip", "dummy") });
        var features = new ProjectFeatures { HasPassword = true, EtsVersion = EtsVersion.Ets5 };

        var act = async () => await ZipHandler.LoadAsync(zip, features, password: null);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*password*");
    }

    [Fact]
    public async Task LoadAsync_PasswordProtected_NoNestedArchive_Throws()
    {
        using var zip = TestZipBuilder.BuildOuter(new[] { ("knx_master.xml", "x") });
        var features = new ProjectFeatures { HasPassword = true, EtsVersion = EtsVersion.Ets5 };

        var act = async () => await ZipHandler.LoadAsync(zip, features, "affe");

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Nested project archive not found*");
    }

    [Fact]
    public void DeriveEts6Password_KnownInput_ReturnsKnownHash()
    {
        // Reference: test-project password "test" in testprojekt-ets6.knxproj uses PBKDF2 result in Base64.
        var result = ZipHandler.DeriveEts6Password("test");

        result.Should().NotBeNullOrEmpty();
        Convert.FromBase64String(result).Should().HaveCount(32);
    }
}
