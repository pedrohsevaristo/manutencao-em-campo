namespace ManutencaoEmCampo.Web.Tickets;

public static class TicketPresentation
{
    private static readonly TimeZoneInfo LocalZone = TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo");

    public static string Label(this TicketStatus status) => status switch
    {
        TicketStatus.Open => "Aberto",
        TicketStatus.InProgress => "Em atendimento",
        TicketStatus.Completed => "Concluído",
        _ => "Desconhecido"
    };

    public static string Label(this TicketPriority priority) => priority switch
    {
        TicketPriority.Low => "Baixa",
        TicketPriority.Normal => "Normal",
        TicketPriority.High => "Alta",
        _ => "Desconhecida"
    };

    public static string Label(this TicketAction action) => action switch
    {
        TicketAction.Created => "Chamado registrado",
        TicketAction.Assigned => "Responsável atribuído",
        TicketAction.Started => "Atendimento iniciado",
        TicketAction.Completed => "Atendimento concluído",
        _ => "Atualização"
    };

    public static string LocalDate(this DateTime utcDate) =>
        TimeZoneInfo.ConvertTimeFromUtc(utcDate, LocalZone).ToString("dd/MM/yyyy 'às' HH:mm");
}
