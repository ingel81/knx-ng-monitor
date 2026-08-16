using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace KnxMonitor.Infrastructure.Tests;

/// <summary>
/// Covers issue #9: setting <c>ASPNETCORE_URLS</c> had no effect, in Docker as well. The shipped
/// appsettings.json pinned <c>Kestrel:Endpoints:Http:Url</c>, and a config-backed Kestrel endpoint
/// outranks the address list the variable feeds — so the app kept listening on 8080 whatever the
/// user asked for. The default now lives in <see cref="HostingUrls"/> and steps aside as soon as
/// anything else configures an address.
/// </summary>
public class HostingUrlsTests
{
    /// <summary>
    /// Reads the real file (linked into the test output by the csproj) rather than going through
    /// configuration layering: the question is whether the SHIPPED file on its own pins an endpoint.
    /// </summary>
    [Fact]
    public void ShippedAppSettings_DoesNotPinTheListenEndpoint()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "ApiConfig", "appsettings.json");
        File.Exists(path).Should().BeTrue($"the API's appsettings.json should be linked to {path}");

        using var document = JsonDocument.Parse(File.ReadAllText(path));

        document.RootElement.TryGetProperty("Kestrel", out _).Should().BeFalse(
            "a Kestrel endpoint in appsettings.json outranks ASPNETCORE_URLS and makes it inert");
    }

    [Theory]
    // Nothing configured anywhere — this is the case the built-in default exists for.
    [InlineData(null, null, null, null, HostingUrls.DefaultListenUrl)]
    // An empty value is not configuration.
    [InlineData("", null, null, null, HostingUrls.DefaultListenUrl)]
    // ASPNETCORE_URLS / DOTNET_URLS / --urls / launchSettings all land in the "urls" key.
    [InlineData("http://127.0.0.1:9000", null, null, null, null)]
    // ASPNETCORE_HTTP_PORTS / ASPNETCORE_HTTPS_PORTS.
    [InlineData(null, "9000", null, null, null)]
    [InlineData(null, null, "9001", null, null)]
    // A hand-added Kestrel endpoint must keep winning — without an "Overriding address(es)" warning.
    [InlineData(null, null, null, "http://127.0.0.1:8099", null)]
    public void ResolveFallbackListenUrl_StepsAsideWheneverAnAddressIsConfigured(
        string? urls, string? httpPorts, string? httpsPorts, string? kestrelUrl, string? expected)
    {
        // null means "key absent" here — a key present with a null value would still show up as a
        // section child and is a different case (covered by the empty-string row for "urls").
        var settings = new Dictionary<string, string?>
        {
            ["urls"] = urls,
            ["HTTP_PORTS"] = httpPorts,
            ["HTTPS_PORTS"] = httpsPorts,
            ["Kestrel:Endpoints:Http:Url"] = kestrelUrl
        }.Where(pair => pair.Value is not null);

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        HostingUrls.ResolveFallbackListenUrl(configuration).Should().Be(expected);
    }

    /// <summary>
    /// LAN/mobile testing and <c>docker run -p 8080:8080</c> all break the moment this is tidied
    /// into a localhost-only binding.
    /// </summary>
    [Fact]
    public void DefaultListenUrl_BindsAllInterfaces()
    {
        HostingUrls.DefaultListenUrl.Should().StartWith("http://0.0.0.0:");
    }

    [Theory]
    [InlineData("http://0.0.0.0:8080", "http://localhost:8080")]
    [InlineData("http://[::]:8080", "http://localhost:8080")]
    [InlineData("http://+:8080", "http://localhost:8080")]
    [InlineData("http://*:8080", "http://localhost:8080")]
    [InlineData("http://127.0.0.1:8099", "http://127.0.0.1:8099")]
    public void ToBrowsableUrl_NormalisesWildcardHostsOnly(string bound, string expected)
    {
        HostingUrls.ToBrowsableUrl(bound).Should().Be(expected);
    }
}
