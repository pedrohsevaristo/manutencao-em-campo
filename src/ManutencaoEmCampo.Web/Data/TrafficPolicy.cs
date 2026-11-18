using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;

namespace ManutencaoEmCampo.Web.Data;

internal static class TrafficPolicy
{
    public const string Login = "login";
    public const string Operations = "operations";
    public const string TicketList = "ticket-list";
    public const int MaxBodyBytes = 32 * 1024;

    public static int Limit(IConfiguration configuration, string name, int fallback)
    {
        var value = configuration.GetValue($"Traffic:{name}", fallback);
        return value > 0 ? value : throw new InvalidOperationException($"Traffic:{name} deve ser positivo.");
    }

    public static string Source(HttpContext context)
    {
        var address = context.Connection.RemoteIpAddress;
        return (address?.IsIPv4MappedToIPv6 == true ? address.MapToIPv4() : address)?.ToString() ?? "local";
    }

    public static string Account(HttpContext context) =>
        context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? Source(context);

    public static FixedWindowRateLimiterOptions Window(int permits) =>
        new() { PermitLimit = permits, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 };

    public static void RetryAfter(HttpResponse response, RateLimitLease lease)
    {
        var seconds = lease.TryGetMetadata(MetadataName.RetryAfter, out var retry)
            ? Math.Max(1, (int)Math.Ceiling(retry.TotalSeconds))
            : 60;
        response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
    }

    public static ForwardedHeadersOptions? TrustedForwarding(IConfiguration configuration)
    {
        var proxies = configuration.GetSection("Traffic:TrustedProxies").Get<string[]>();
        if (proxies is not { Length: > 0 })
            return null;

        var options = new ForwardedHeadersOptions
        {
            ForwardedHeaders = ForwardedHeaders.XForwardedFor,
            ForwardLimit = 1
        };
        options.KnownIPNetworks.Clear();
        options.KnownProxies.Clear();
        foreach (var proxy in proxies)
        {
            if (!IPAddress.TryParse(proxy, out var address)
                || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
                throw new InvalidOperationException("Traffic:TrustedProxies deve conter somente endereços IP exatos.");
            options.KnownProxies.Add(address);
        }
        return options;
    }
}

public sealed class LoginAttempts : IDisposable
{
    private readonly PartitionedRateLimiter<(string Source, string Email)> limiter;

    public LoginAttempts(IConfiguration configuration)
    {
        var attempts = TrafficPolicy.Limit(configuration, "LoginPerAccountAndSource", 8);
        limiter = PartitionedRateLimiter.Create<(string Source, string Email), (string Source, string Email)>(
            key => RateLimitPartition.GetFixedWindowLimiter(key, _ => TrafficPolicy.Window(attempts)));
    }

    public RateLimitLease Acquire(HttpContext context, string normalizedEmail) =>
        limiter.AttemptAcquire((TrafficPolicy.Source(context), normalizedEmail));

    public void Dispose() => limiter.Dispose();
}
