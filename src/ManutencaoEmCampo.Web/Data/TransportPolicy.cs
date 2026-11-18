using System.Net;

namespace ManutencaoEmCampo.Web.Data;

internal static class TransportPolicy
{
    public static string[] AllowedHosts(string? value)
    {
        var hosts = value?.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (hosts is not { Length: > 0 } || hosts.Any(host => host.Contains('*')
            || Uri.CheckHostName(host) == UriHostNameType.Unknown
            || Uri.CheckHostName(host) == UriHostNameType.IPv6 && !(host.StartsWith('[') && host.EndsWith(']'))))
            throw new InvalidOperationException("AllowedHosts deve listar nomes ou IPs exatos, sem curingas nem portas.");

        return hosts;
    }

    public static bool IsLocalHttp(HttpContext context) =>
        !context.Request.IsHttps
        && IsLoopback(context.Connection.LocalIpAddress)
        && IsLoopback(context.Connection.RemoteIpAddress)
        && (context.Request.Host.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || IPAddress.TryParse(context.Request.Host.Host, out var host) && IsLoopback(host));

    public static string? MobileOrigin(string? value, bool allowLocalHttp)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (!Uri.TryCreate(value, UriKind.Absolute, out var origin)
            || origin.Host.Length == 0 || origin.UserInfo.Length != 0
            || origin.PathAndQuery != "/" || origin.Fragment.Length != 0
            || (origin.Scheme != Uri.UriSchemeHttps
                && !(allowLocalHttp && origin.Scheme == Uri.UriSchemeHttp && origin.IsLoopback)))
            throw new InvalidOperationException("Mobile:WebOrigin deve informar uma origem HTTPS ou uma origem HTTP local na demonstração.");

        return origin.GetLeftPart(UriPartial.Authority);
    }

    private static bool IsLoopback(IPAddress? address) => address is not null
        && IPAddress.IsLoopback(address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address);
}
