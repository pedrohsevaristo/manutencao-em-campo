using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;

namespace ManutencaoEmCampo.Web.Data;

internal sealed class WorkUserStore(WorkDbContext db, IdentityErrorDescriber errors)
    : UserStore<WorkUser, IdentityRole, WorkDbContext>(db, errors)
{
    public override async Task AddToRoleAsync(WorkUser user, string normalizedRoleName,
        CancellationToken cancellationToken = default)
    {
        await base.AddToRoleAsync(user, normalizedRoleName, cancellationToken);
        user.SecurityStamp = Guid.NewGuid().ToString("N");
    }

    public override async Task RemoveFromRoleAsync(WorkUser user, string normalizedRoleName,
        CancellationToken cancellationToken = default)
    {
        await base.RemoveFromRoleAsync(user, normalizedRoleName, cancellationToken);
        user.SecurityStamp = Guid.NewGuid().ToString("N");
    }
}
