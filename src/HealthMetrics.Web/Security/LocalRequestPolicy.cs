using System.Net;
using Microsoft.AspNetCore.Http;

namespace HealthMetrics.Web.Security;

internal static class LocalRequestPolicy
{
    public static bool IsLocal(HttpContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var remoteIp = context.Connection.RemoteIpAddress;
        if (remoteIp is null)
        {
            return true;
        }

        if (remoteIp.IsIPv4MappedToIPv6)
        {
            remoteIp = remoteIp.MapToIPv4();
        }

        return IPAddress.IsLoopback(remoteIp);
    }
}
