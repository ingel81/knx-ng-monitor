using Microsoft.Extensions.Configuration;

namespace KnxMonitor.Infrastructure;

/// <summary>
/// Decides which address the API listens on when nothing else says.
/// <para>
/// Up to 0.10.0 the shipped appsettings.json carried a <c>Kestrel:Endpoints:Http:Url</c> entry.
/// A config-backed Kestrel endpoint outranks the address list, so <c>ASPNETCORE_URLS</c> was
/// silently ignored — in Docker as well, where the image sets it (issue #9). The default therefore
/// lives here in code and only applies when the address list is genuinely empty.
/// </para>
/// <para>
/// The Kestrel:Endpoints guard in <see cref="ResolveFallbackListenUrl"/> is not cosmetic: without
/// it we would populate the address list while Kestrel still binds the configured endpoint, which
/// produces an "Overriding address(es)" warning and — worse — a startup banner and auto-opened
/// browser pointing at an address nothing listens on.
/// </para>
/// <para>
/// The default binds all interfaces, never localhost. Testing from a phone in the LAN and
/// <c>docker run -p 8080:8080</c> both depend on it.
/// </para>
/// </summary>
public static class HostingUrls
{
    public const string DefaultListenUrl = "http://0.0.0.0:8080";

    /// <summary>
    /// Returns <see cref="DefaultListenUrl"/> when no address was configured anywhere, otherwise
    /// null — meaning "hands off, someone else decides".
    /// <para>
    /// The keys checked are the ones the host itself reads after the <c>ASPNETCORE_</c> /
    /// <c>DOTNET_</c> prefix has been stripped: <c>urls</c> (ASPNETCORE_URLS, DOTNET_URLS,
    /// <c>--urls</c>, launchSettings' applicationUrl and an appsettings <c>Urls</c> key all land
    /// there), <c>HTTP_PORTS</c> / <c>HTTPS_PORTS</c>, and any <c>Kestrel:Endpoints</c> child.
    /// </para>
    /// </summary>
    public static string? ResolveFallbackListenUrl(IConfiguration configuration)
    {
        if (!string.IsNullOrWhiteSpace(configuration["urls"])
            || !string.IsNullOrWhiteSpace(configuration["HTTP_PORTS"])
            || !string.IsNullOrWhiteSpace(configuration["HTTPS_PORTS"])
            || configuration.GetSection("Kestrel:Endpoints").GetChildren().Any())
        {
            return null;
        }

        return DefaultListenUrl;
    }

    /// <summary>
    /// Rewrites the wildcard host of a bound address to localhost so it can be clicked in a
    /// terminal or handed to a browser. Kestrel reports every one of these forms depending on how
    /// the address was written — the container image uses <c>http://+:8080</c>, which surfaces as
    /// <c>http://[::]:8080</c>.
    /// <para>
    /// Plain string replacement, not <see cref="Uri"/> parsing: the display names always carry the
    /// port, and <c>+</c> / <c>*</c> are not legal Uri hosts, so parsing would throw on them.
    /// </para>
    /// </summary>
    public static string ToBrowsableUrl(string url) => url
        .Replace("://0.0.0.0:", "://localhost:", StringComparison.Ordinal)
        .Replace("://[::]:", "://localhost:", StringComparison.Ordinal)
        .Replace("://+:", "://localhost:", StringComparison.Ordinal)
        .Replace("://*:", "://localhost:", StringComparison.Ordinal);
}
