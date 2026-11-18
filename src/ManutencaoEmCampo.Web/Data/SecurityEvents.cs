using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;

namespace ManutencaoEmCampo.Web.Data;

internal enum SecurityEvent
{
    AuthenticationSucceeded = 4600,
    AuthenticationFailed = 4601,
    AuthenticationRequired = 4602,
    AuthorizationDenied = 4603,
    LoginBlocked = 4604,
    SessionRevoked = 4605,
    SessionInvalidated = 4606,
    RequestLimited = 4607,
    RequestBodyRejected = 4608
}

public sealed class SecurityEvents : IDisposable
{
    private readonly ILogger<SecurityEvents> logger;
    private readonly IHttpContextAccessor context;
    private readonly Meter meter = new("ManutencaoEmCampo.Security");
    private readonly Counter<long> counter;

    public SecurityEvents(ILogger<SecurityEvents> logger, IHttpContextAccessor context)
    {
        this.logger = logger;
        this.context = context;
        counter = meter.CreateCounter<long>("manutencao.security.events", unit: "{event}");
    }

    internal void Record(SecurityEvent kind, string surface)
    {
        var name = kind.ToString();
        var level = kind is SecurityEvent.AuthenticationSucceeded or SecurityEvent.SessionRevoked
            or SecurityEvent.AuthenticationRequired ? LogLevel.Information : LogLevel.Warning;
        logger.Log(level, new EventId((int)kind, name), "Evento de segurança {SecurityEvent} em {Surface}. TraceId {TraceId}",
            name, surface, Activity.Current?.TraceId.ToString() ?? context.HttpContext?.TraceIdentifier);
        counter.Add(1, new KeyValuePair<string, object?>("event", name),
            new KeyValuePair<string, object?>("surface", surface));
    }

    internal static string Surface(HttpContext context) =>
        context.Request.Path.StartsWithSegments("/api") ? "api" : "mvc";

    public void Dispose() => meter.Dispose();
}

internal sealed class AuthorizationEvents(SecurityEvents events) : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler fallback = new();

    public Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy,
        PolicyAuthorizationResult result)
    {
        if (result.Forbidden)
            events.Record(SecurityEvent.AuthorizationDenied, SecurityEvents.Surface(context));
        else if (result.Challenged)
            events.Record(SecurityEvent.AuthenticationRequired, SecurityEvents.Surface(context));

        return fallback.HandleAsync(next, context, policy, result);
    }
}
