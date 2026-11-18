using System.Linq.Expressions;
using ManutencaoEmCampo.Web.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ManutencaoEmCampo.Web.Tickets;

public sealed class TicketService(WorkDbContext db, UserManager<WorkUser> users, TimeProvider clock)
{
    private static readonly Expression<Func<Ticket, TicketSummary>> ToSummary = ticket =>
        new TicketSummary(ticket.Id, ticket.Title, ticket.Location, ticket.Priority,
            ticket.Status, ticket.CreatedAt, ticket.AssignedToName);

    public async Task<TicketPage> ListAsync(Actor actor, TicketFilter filter, CancellationToken cancellationToken)
    {
        var query = VisibleTo(actor).AsNoTracking();
        if (filter.Status is { } status)
            query = query.Where(ticket => ticket.Status == status);

        var total = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(ticket => ticket.CreatedAt).ThenByDescending(ticket => ticket.Id)
            .Skip((filter.Page - 1) * filter.PageSize).Take(filter.PageSize)
            .Select(ToSummary).ToListAsync(cancellationToken);
        return new TicketPage(items, filter.Page, filter.PageSize, total);
    }

    public Task<TicketDetails?> FindAsync(int id, Actor actor, CancellationToken cancellationToken) =>
        VisibleTo(actor).AsNoTracking().Where(ticket => ticket.Id == id).Select(ticket =>
            new TicketDetails(new TicketSummary(ticket.Id, ticket.Title, ticket.Location, ticket.Priority,
                ticket.Status, ticket.CreatedAt, ticket.AssignedToName), ticket.Description, ticket.AssignedToId,
                ticket.History.OrderBy(entry => entry.OccurredAt).ThenBy(entry => entry.Id)
                    .Select(entry => new TicketTimeline(entry.Action, entry.Actor, entry.Note, entry.OccurredAt)).ToList()))
            .SingleOrDefaultAsync(cancellationToken);

    public async Task<int> CreateAsync(CreateTicket input, Actor actor, CancellationToken cancellationToken)
    {
        if (input.Title.Trim().Length < 5 || input.Description.Trim().Length < 10)
            throw new TicketConflict("Informe um título e uma descrição com pelo menos 5 e 10 caracteres, respectivamente.");

        var ticket = Ticket.Create(input, actor, clock.GetUtcNow().UtcDateTime);
        db.Tickets.Add(ticket);
        await db.SaveChangesAsync(cancellationToken);
        return ticket.Id;
    }

    public async Task<bool> AssignAsync(int id, string technicianId, Actor actor, CancellationToken cancellationToken)
    {
        var ticket = await ForUpdateAsync(id, actor, cancellationToken);
        if (ticket is null)
            return false;

        var technician = await users.FindByIdAsync(technicianId);
        if (technician is null || !await users.IsInRoleAsync(technician, Roles.Technician))
            throw new TicketConflict("Selecione um técnico cadastrado.");

        ticket.Assign(technician.Id, technician.DisplayName, actor, clock.GetUtcNow().UtcDateTime);
        await SaveChangeAsync(cancellationToken);
        return true;
    }

    public Task<bool> StartAsync(int id, Actor actor, CancellationToken cancellationToken) =>
        UpdateAsync(id, actor, (ticket, now) => ticket.Start(actor, now), cancellationToken);

    public Task<bool> CompleteAsync(int id, string note, Actor actor, CancellationToken cancellationToken) =>
        UpdateAsync(id, actor, (ticket, now) => ticket.Complete(actor, note, now), cancellationToken);

    public async Task<IReadOnlyList<TechnicianOption>> TechniciansAsync() =>
        (await users.GetUsersInRoleAsync(Roles.Technician)).OrderBy(user => user.DisplayName)
            .Select(user => new TechnicianOption(user.Id, user.DisplayName)).ToList();

    private IQueryable<Ticket> VisibleTo(Actor actor) =>
        actor.IsSupervisor ? db.Tickets : db.Tickets.Where(ticket => ticket.AssignedToId == actor.Id);

    private Task<Ticket?> ForUpdateAsync(int id, Actor actor, CancellationToken cancellationToken) =>
        VisibleTo(actor).SingleOrDefaultAsync(ticket => ticket.Id == id, cancellationToken);

    private async Task<bool> UpdateAsync(int id, Actor actor, Action<Ticket, DateTime> change,
        CancellationToken cancellationToken)
    {
        var ticket = await ForUpdateAsync(id, actor, cancellationToken);
        if (ticket is null)
            return false;

        change(ticket, clock.GetUtcNow().UtcDateTime);
        await SaveChangeAsync(cancellationToken);
        return true;
    }

    private async Task SaveChangeAsync(CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new TicketConflict("Este chamado mudou durante a operação. Atualize a tela e tente novamente.");
        }
    }
}
