namespace ManutencaoEmCampo.Web.Tickets;

public enum TicketStatus { Open = 1, InProgress, Completed }
public enum TicketPriority { Low = 1, Normal, High }
public enum TicketAction { Created = 1, Assigned, Started, Completed }

public sealed class Ticket
{
    private Ticket() { }

    public int Id { get; private set; }
    public string Title { get; private set; } = "";
    public string Location { get; private set; } = "";
    public string Description { get; private set; } = "";
    public TicketPriority Priority { get; private set; }
    public TicketStatus Status { get; private set; }
    public DateTime CreatedAt { get; private set; }
    public string? AssignedToId { get; private set; }
    public string? AssignedToName { get; private set; }
    public Guid Version { get; private set; } = Guid.NewGuid();
    public List<TicketEvent> History { get; private set; } = [];

    public static Ticket Create(CreateTicket input, Actor actor, DateTime now)
    {
        var ticket = new Ticket
        {
            Title = input.Title.Trim(),
            Location = input.Location.Trim(),
            Description = input.Description.Trim(),
            Priority = input.Priority,
            CreatedAt = now,
            Status = TicketStatus.Open
        };
        ticket.Record(TicketAction.Created, actor, null, now);
        return ticket;
    }

    public void Assign(string technicianId, string technicianName, Actor actor, DateTime now)
    {
        if (Status != TicketStatus.Open)
            throw new TicketConflict("A atribuição só pode mudar enquanto o chamado está aberto.");

        if (AssignedToId == technicianId)
            return;

        AssignedToId = technicianId;
        AssignedToName = technicianName;
        Record(TicketAction.Assigned, actor, technicianName, now);
    }

    public void Start(Actor actor, DateTime now)
    {
        EnsureStatus(TicketStatus.Open, "Apenas um chamado aberto pode ser iniciado.");
        Status = TicketStatus.InProgress;
        Record(TicketAction.Started, actor, null, now);
    }

    public void Complete(Actor actor, string note, DateTime now)
    {
        EnsureStatus(TicketStatus.InProgress, "Inicie o atendimento antes de concluir o chamado.");
        var cleanNote = note.Trim();
        if (cleanNote.Length is < 10 or > 1000)
            throw new TicketConflict("Descreva o serviço realizado em 10 a 1.000 caracteres.");

        Status = TicketStatus.Completed;
        Record(TicketAction.Completed, actor, cleanNote, now);
    }

    private void EnsureStatus(TicketStatus expected, string message)
    {
        if (Status != expected)
            throw new TicketConflict(message);
    }

    private void Record(TicketAction action, Actor actor, string? note, DateTime now)
    {
        Version = Guid.NewGuid();
        History.Add(new TicketEvent { Action = action, Actor = actor.Name, Note = note, OccurredAt = now });
    }
}

public sealed class TicketEvent
{
    public int Id { get; private set; }
    public int TicketId { get; private set; }
    public TicketAction Action { get; init; }
    public string Actor { get; init; } = "";
    public string? Note { get; init; }
    public DateTime OccurredAt { get; init; }
}

public sealed class TicketConflict(string message) : Exception(message);
