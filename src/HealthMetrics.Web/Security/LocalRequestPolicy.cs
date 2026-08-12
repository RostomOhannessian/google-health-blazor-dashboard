using System.Net;
using Microsoft.AspNetCore.Http;

namespace HealthMetrics.Web.Security;

internal static class LocalRequestPolicy
{
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
}
