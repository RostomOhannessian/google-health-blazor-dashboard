using System.Net;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace HealthMetrics.Web.Security;

internal static class LocalRequestPolicy
{
    /// <summary>
    /// Address blocks a <c>LocalRequestPolicy:TrustedNetworks</c> entry may fall inside.
    /// The allowlist exists only so a container bridge gateway can reach the app, so any
    /// entry outside private, loopback, link-local, or unique-local space is a
    /// misconfiguration that would expose an unauthenticated dashboard to a wider network.
    /// </summary>
    private static readonly IPNetwork[] AllowedTrustBlocks =
    [
        new(IPAddress.Parse("10.0.0.0"), 8),
        new(IPAddress.Parse("172.16.0.0"), 12),
        new(IPAddress.Parse("192.168.0.0"), 16),
        new(IPAddress.Parse("127.0.0.0"), 8),
        new(IPAddress.Parse("169.254.0.0"), 16),
        new(IPAddress.Parse("fc00::"), 7),
        new(IPAddress.Parse("fe80::"), 10),
        new(IPAddress.Parse("::1"), 128)
    ];

    /// <summary>
    /// Returns true only for genuine loopback requests. Use this overload outside of
    /// containerized/reverse-proxied deployments where the app's own network stack
    /// sees the real client address.
    /// </summary>
    public static bool IsLocal(HttpContext context) => IsLocal(context, []);

    /// <summary>
    /// Returns true for loopback requests or for requests whose remote address falls
    /// within one of <paramref name="trustedNetworks"/>. The allowlist exists so a
    /// container's published-port gateway address (which never appears as loopback
    /// to the app) can be explicitly opted in via
    /// <c>LocalRequestPolicy:TrustedNetworks</c> configuration. It is empty by
    /// default, so behavior outside of an explicit opt-in is unchanged.
    /// </summary>
    public static bool IsLocal(HttpContext context, IReadOnlyList<IPNetwork> trustedNetworks)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(trustedNetworks);

        var remoteIp = context.Connection.RemoteIpAddress;
        if (remoteIp is null)
        {
            return false;
        }

        if (remoteIp.IsIPv4MappedToIPv6)
        {
            remoteIp = remoteIp.MapToIPv4();
        }

        if (IPAddress.IsLoopback(remoteIp))
        {
            return true;
        }

        foreach (var network in trustedNetworks)
        {
            if (network.Contains(remoteIp))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Parses <c>LocalRequestPolicy:TrustedNetworks</c> CIDR entries into networks that are
    /// trusted in addition to loopback. Entries that are not valid CIDR, or that reach
    /// outside <see cref="AllowedTrustBlocks"/>, are dropped with a logged warning so a
    /// typo or an over-broad range such as <c>0.0.0.0/0</c> cannot silently expose the app.
    /// </summary>
    public static IReadOnlyList<IPNetwork> ParseTrustedNetworks(
        IReadOnlyList<string>? rawNetworks,
        ILogger logger)
    {
        ArgumentNullException.ThrowIfNull(logger);

        if (rawNetworks is null || rawNetworks.Count == 0)
        {
            return [];
        }

        var parsedNetworks = new List<IPNetwork>(rawNetworks.Count);
        foreach (var rawNetwork in rawNetworks)
        {
            if (!IPNetwork.TryParse(rawNetwork, out var network))
            {
                logger.LogWarning(
                    "Ignoring invalid LocalRequestPolicy:TrustedNetworks entry {RawNetwork}. Use CIDR notation, e.g. 172.16.0.0/12.",
                    rawNetwork);
                continue;
            }

            if (!IsPrivateNetwork(network))
            {
                logger.LogWarning(
                    "Ignoring LocalRequestPolicy:TrustedNetworks entry {RawNetwork} because it reaches outside private, loopback, or link-local address space. Only a local container bridge range may be trusted.",
                    rawNetwork);
                continue;
            }

            parsedNetworks.Add(network);
        }

        if (parsedNetworks.Count > 0)
        {
            logger.LogInformation(
                "Local request policy will also trust {TrustedNetworkCount} configured network(s) in addition to loopback.",
                parsedNetworks.Count);
        }

        return parsedNetworks;
    }

    /// <summary>
    /// Returns true only when every address in <paramref name="network"/> falls inside a
    /// single allowed block. Comparing prefix lengths is what rejects a range that merely
    /// starts in private space but widens beyond it, such as <c>172.0.0.0/8</c>.
    /// </summary>
    private static bool IsPrivateNetwork(IPNetwork network)
    {
        foreach (var allowedBlock in AllowedTrustBlocks)
        {
            if (allowedBlock.BaseAddress.AddressFamily == network.BaseAddress.AddressFamily
                && network.PrefixLength >= allowedBlock.PrefixLength
                && allowedBlock.Contains(network.BaseAddress))
            {
                return true;
            }
        }

        return false;
    }
}
