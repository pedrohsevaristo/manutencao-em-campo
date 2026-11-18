using ManutencaoEmCampo.Web.Tickets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ManutencaoEmCampo.Web.Controllers;

[Authorize(Roles = Roles.Supervisor)]
[AutoValidateAntiforgeryToken]
public sealed class WorkOrdersController(TicketService tickets) : Controller
{
    private Actor CurrentActor => Actor.From(User);

    [HttpGet("/")]
    public async Task<IActionResult> Index([FromQuery] TicketFilter filter, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return BadRequest("Filtro de chamados inválido.");

        ViewData["Status"] = filter.Status;
        return View(await tickets.ListAsync(CurrentActor, filter, cancellationToken));
    }

    [HttpGet("/chamados/novo")]
    public IActionResult Create() => View(new CreateTicket());

    [HttpPost("/chamados/novo")]
    public async Task<IActionResult> Create(CreateTicket input, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return View(input);

        try
        {
            var id = await tickets.CreateAsync(input, CurrentActor, cancellationToken);
            TempData["Notice"] = "Chamado registrado.";
            return RedirectToAction(nameof(Details), new { id });
        }
        catch (TicketConflict conflict)
        {
            ModelState.AddModelError("", conflict.Message);
            return View(input);
        }
    }

    [HttpGet("/chamados/{id:int}")]
    public async Task<IActionResult> Details(int id, CancellationToken cancellationToken) =>
        await DetailsView(id, cancellationToken);

    [HttpPost("/chamados/{id:int}/responsavel")]
    public async Task<IActionResult> Assign(int id, AssignTicket input, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return await DetailsView(id, cancellationToken);

        try
        {
            if (!await tickets.AssignAsync(id, input.TechnicianId, CurrentActor, cancellationToken))
                return NotFound();

            TempData["Notice"] = "Responsável atualizado.";
            return RedirectToAction(nameof(Details), new { id });
        }
        catch (TicketConflict conflict)
        {
            ModelState.AddModelError("", conflict.Message);
            return await DetailsView(id, cancellationToken);
        }
    }

    private async Task<IActionResult> DetailsView(int id, CancellationToken cancellationToken)
    {
        var details = await tickets.FindAsync(id, CurrentActor, cancellationToken);
        if (details is null)
            return NotFound();

        var technicians = details.Ticket.Status == TicketStatus.Open ? await tickets.TechniciansAsync() : [];
        return View(nameof(Details), new WorkOrderDetails(details, technicians));
    }
}

public sealed record WorkOrderDetails(TicketDetails Details, IReadOnlyList<TechnicianOption> Technicians);
