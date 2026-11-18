using ManutencaoEmCampo.Web.Data;
using ManutencaoEmCampo.Web.Tickets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ManutencaoEmCampo.Web.Controllers;

[ApiController]
[Authorize(AuthenticationSchemes = SessionSchemes.Api, Roles = Roles.Operators)]
[Route("api/chamados")]
[EnableRateLimiting(TrafficPolicy.Operations)]
public sealed class TicketsApiController(TicketService tickets) : ControllerBase
{
    private Actor CurrentActor => Actor.From(User);

    [HttpGet, EnableRateLimiting(TrafficPolicy.TicketList)]
    public async Task<ActionResult<TicketPage>> List([FromQuery] TicketFilter filter, CancellationToken cancellationToken) =>
        await tickets.ListAsync(CurrentActor, filter, cancellationToken);

    [HttpGet("{id:int}")]
    public async Task<ActionResult<TicketDetails>> Details(int id, CancellationToken cancellationToken) =>
        await tickets.FindAsync(id, CurrentActor, cancellationToken) is { } result ? Ok(result) : NotFound();

    [HttpGet("/api/tecnicos"), Authorize(Roles = Roles.Supervisor)]
    public async Task<IReadOnlyList<TechnicianOption>> Technicians() => await tickets.TechniciansAsync();

    [HttpPost, Authorize(Roles = Roles.Supervisor)]
    public async Task<IActionResult> Create(CreateTicket input, CancellationToken cancellationToken)
    {
        try
        {
            var id = await tickets.CreateAsync(input, CurrentActor, cancellationToken);
            return Created(Url.Action(nameof(Details), new { id })!, new { id });
        }
        catch (TicketConflict conflict)
        {
            return Problem(statusCode: 400, title: conflict.Message);
        }
    }

    [HttpPut("{id:int}/responsavel"), Authorize(Roles = Roles.Supervisor)]
    public Task<IActionResult> Assign(int id, AssignTicket input, CancellationToken cancellationToken) =>
        Change(() => tickets.AssignAsync(id, input.TechnicianId, CurrentActor, cancellationToken));

    [HttpPost("{id:int}/inicio"), Authorize(Roles = Roles.Technician)]
    public Task<IActionResult> Start(int id, CancellationToken cancellationToken) =>
        Change(() => tickets.StartAsync(id, CurrentActor, cancellationToken));

    [HttpPost("{id:int}/conclusao"), Authorize(Roles = Roles.Technician)]
    public Task<IActionResult> Complete(int id, CompleteTicket input, CancellationToken cancellationToken) =>
        Change(() => tickets.CompleteAsync(id, input.Note, CurrentActor, cancellationToken));

    private async Task<IActionResult> Change(Func<Task<bool>> operation)
    {
        try
        {
            return await operation() ? NoContent() : NotFound();
        }
        catch (TicketConflict conflict)
        {
            return Problem(statusCode: 409, title: conflict.Message);
        }
    }
}
