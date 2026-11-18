using System.ComponentModel.DataAnnotations;
using System.Security.Claims;

namespace ManutencaoEmCampo.Web.Tickets;

public static class Roles
{
    public const string Supervisor = "Supervisor";
    public const string Technician = "Technician";
    public const string Operators = Supervisor + "," + Technician;
}

public sealed record Actor(string Id, string Name, bool IsSupervisor)
{
    public static Actor From(ClaimsPrincipal principal) => new(
        principal.FindFirstValue(ClaimTypes.NameIdentifier)!,
        principal.FindFirstValue("display_name") ?? principal.Identity!.Name!,
        principal.IsInRole(Roles.Supervisor));
}

public sealed class CreateTicket
{
    [Required(ErrorMessage = "Informe o título."), StringLength(100, MinimumLength = 5, ErrorMessage = "Use de 5 a 100 caracteres.")]
    [Display(Name = "Título")]
    public string Title { get; init; } = "";

    [Required(ErrorMessage = "Informe o local ou equipamento."), StringLength(100)]
    [Display(Name = "Local ou equipamento")]
    public string Location { get; init; } = "";

    [Required(ErrorMessage = "Descreva o problema."), StringLength(1000, MinimumLength = 10, ErrorMessage = "Use de 10 a 1.000 caracteres.")]
    [Display(Name = "Descrição")]
    public string Description { get; init; } = "";

    [EnumDataType(typeof(TicketPriority), ErrorMessage = "Escolha uma prioridade válida.")]
    [Display(Name = "Prioridade")]
    public TicketPriority Priority { get; init; } = TicketPriority.Normal;
}

public sealed class AssignTicket
{
    [Required(ErrorMessage = "Selecione um técnico."), StringLength(450)]
    public string TechnicianId { get; init; } = "";
}

public sealed class CompleteTicket
{
    [Required(ErrorMessage = "Informe o serviço realizado.")]
    [StringLength(1000, MinimumLength = 10, ErrorMessage = "Use de 10 a 1.000 caracteres.")]
    public string Note { get; init; } = "";
}

public sealed class TicketFilter
{
    [EnumDataType(typeof(TicketStatus))]
    public TicketStatus? Status { get; init; }

    [Range(1, 10000)]
    public int Page { get; init; } = 1;

    [Range(1, 50)]
    public int PageSize { get; init; } = 10;
}

public sealed record TicketSummary(int Id, string Title, string Location, TicketPriority Priority,
    TicketStatus Status, DateTime CreatedAt, string? AssignedToName);
public sealed record TicketTimeline(TicketAction Action, string Actor, string? Note, DateTime OccurredAt);
public sealed record TicketDetails(TicketSummary Ticket, string Description, string? AssignedToId,
    IReadOnlyList<TicketTimeline> History);
public sealed record TicketPage(IReadOnlyList<TicketSummary> Items, int Page, int PageSize, int Total);
public sealed record TechnicianOption(string Id, string Name);
