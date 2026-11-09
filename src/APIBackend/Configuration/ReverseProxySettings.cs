using System.Net;
using Microsoft.AspNetCore.HttpOverrides;

namespace DatasetStudio.APIBackend.Configuration;

/// <summary>
/// Which reverse proxies the API believes about the client's address (configuration section
/// <c>ReverseProxy</c>: <c>KnownProxies</c> are IP addresses, <c>KnownNetworks</c> are CIDR ranges).
/// Behind Caddy every request comes from Caddy, so the per-IP login and registration limits need
/// the address Caddy passes in <c>X-Forwarded-For</c>. That header was first trusted from any
/// private-network address, which let a LAN client reaching the API's published port directly
/// send a different address with each request and get a fresh rate-limit allowance every time.
/// Nothing is trusted unless configured; the production compose file names Caddy's fixed address.
/// </summary>
public static class ReverseProxySettings
{
    /// <summary>The configuration section.</summary>
    public const string SectionName = "ReverseProxy";

    /// <summary>Fills <paramref name="options"/> from configuration. Throws on an entry that doesn't parse.</summary>
    public static void Apply(ForwardedHeadersOptions options, IConfiguration configuration)
    {
        string[] proxies = configuration.GetSection($"{SectionName}:KnownProxies").Get<string[]>() ?? Array.Empty<string>();
        string[] networks = configuration.GetSection($"{SectionName}:KnownNetworks").Get<string[]>() ?? Array.Empty<string>();

        // The defaults trust loopback; nothing is trusted that isn't listed.
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();
        foreach (string proxy in proxies.Where(p => !string.IsNullOrWhiteSpace(p)))
        {
            if (!IPAddress.TryParse(proxy.Trim(), out IPAddress? address))
            {
                throw new InvalidOperationException($"{SectionName}:KnownProxies contains '{proxy}', which isn't an IP address.");
            }
            options.KnownProxies.Add(address);
        }
        foreach (string network in networks.Where(n => !string.IsNullOrWhiteSpace(n)))
        {
            if (!System.Net.IPNetwork.TryParse(network.Trim(), out System.Net.IPNetwork parsed))
            {
                throw new InvalidOperationException($"{SectionName}:KnownNetworks contains '{network}', which isn't a CIDR range such as 10.0.0.0/8.");
            }
            options.KnownIPNetworks.Add(parsed);
        }

        bool anyTrusted = options.KnownProxies.Count > 0 || options.KnownIPNetworks.Count > 0;
        options.ForwardedHeaders = anyTrusted
            ? ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto
            : ForwardedHeaders.None;
        options.ForwardLimit = 1;
    }
}
