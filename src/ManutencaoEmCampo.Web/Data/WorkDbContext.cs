using ManutencaoEmCampo.Web.Tickets;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ManutencaoEmCampo.Web.Data;

public sealed class WorkUser : IdentityUser
{
    public string DisplayName { get; set; } = "";
}

public sealed class WorkDbContext(DbContextOptions<WorkDbContext> options)
    : IdentityDbContext<WorkUser>(options)
{
    public DbSet<Ticket> Tickets => Set<Ticket>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        var readUtc = new ValueConverter<DateTime, DateTime>(
            value => value, value => DateTime.SpecifyKind(value, DateTimeKind.Utc));

        builder.Entity<WorkUser>().Property(user => user.DisplayName).HasMaxLength(100).IsRequired();
        builder.Entity<Ticket>(ticket =>
        {
            ticket.Property(item => item.Title).HasMaxLength(100).IsRequired();
            ticket.Property(item => item.Location).HasMaxLength(100).IsRequired();
            ticket.Property(item => item.Description).HasMaxLength(1000).IsRequired();
            ticket.Property(item => item.AssignedToName).HasMaxLength(100);
            ticket.Property(item => item.CreatedAt).HasConversion(readUtc);
            ticket.Property(item => item.Version).IsConcurrencyToken();
            ticket.HasOne<WorkUser>().WithMany().HasForeignKey(item => item.AssignedToId)
                .OnDelete(DeleteBehavior.Restrict);
            ticket.HasIndex(item => new { item.CreatedAt, item.Id });
            ticket.HasIndex(item => new { item.Status, item.CreatedAt, item.Id });
            ticket.HasIndex(item => new { item.AssignedToId, item.CreatedAt, item.Id });
        });
        builder.Entity<TicketEvent>(entry =>
        {
            entry.Property(item => item.Actor).HasMaxLength(100).IsRequired();
            entry.Property(item => item.Note).HasMaxLength(1000);
            entry.Property(item => item.OccurredAt).HasConversion(readUtc);
        });
    }
}
