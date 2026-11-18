using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ManutencaoEmCampo.Web.Data;

public sealed class WorkSession
{
    public Guid Id { get; init; }
    public required string UserId { get; init; }
    public WorkUser User { get; init; } = null!;
    public DateTime ExpiresAt { get; init; }
}

public sealed class ActiveSessions(WorkDbContext db, UserManager<WorkUser> users,
    IOptions<IdentityOptions> identity, TimeProvider clock, SecurityEvents events)
{
    public static readonly TimeSpan BearerLifetime = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan CookieLifetime = TimeSpan.FromHours(8);
    private const string SessionClaim = "maintenance_session";

    public async Task<Claim> CreateAsync(WorkUser user, TimeSpan lifetime, CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        await db.Sessions.Where(session => session.ExpiresAt <= now).ExecuteDeleteAsync(cancellationToken);
        var entry = new WorkSession { Id = Guid.NewGuid(), UserId = user.Id, ExpiresAt = now.Add(lifetime) };
        db.Sessions.Add(entry);
        await db.SaveChangesAsync(cancellationToken);
        return new Claim(SessionClaim, entry.Id.ToString("D"));
    }

    public async Task<bool> ValidateAsync(ClaimsPrincipal? principal, CancellationToken cancellationToken)
    {
        if (principal is null)
            return false;

        var claims = identity.Value.ClaimsIdentity;
        var userId = principal.FindFirstValue(claims.UserIdClaimType);
        if (userId is null || !Guid.TryParse(principal.FindFirstValue(SessionClaim), out var id))
            return false;

        var now = clock.GetUtcNow().UtcDateTime;
        var user = await db.Sessions.AsNoTracking()
            .Where(session => session.Id == id && session.UserId == userId && session.ExpiresAt > now)
            .Select(session => session.User)
            .SingleOrDefaultAsync(cancellationToken);
        if (user is null)
            return false;

        var stamp = principal.FindFirstValue(claims.SecurityStampClaimType);
        var roles = principal.FindAll(claims.RoleClaimType).Select(role => role.Value).ToHashSet(StringComparer.Ordinal);
        if (user.IsActive && stamp is not null && stamp == user.SecurityStamp
            && roles.SetEquals(await users.GetRolesAsync(user)))
            return true;

        if (await DeleteAsync(id, userId, cancellationToken) > 0)
            events.Record(SecurityEvent.SessionInvalidated, "session");
        return false;
    }

    public async Task RevokeAsync(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var userId = principal.FindFirstValue(identity.Value.ClaimsIdentity.UserIdClaimType);
        if (userId is not null && Guid.TryParse(principal.FindFirstValue(SessionClaim), out var id)
            && await DeleteAsync(id, userId, cancellationToken) > 0)
            events.Record(SecurityEvent.SessionRevoked, "session");
    }

    private Task<int> DeleteAsync(Guid id, string userId, CancellationToken cancellationToken) =>
        db.Sessions.Where(session => session.Id == id && session.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);
}
