using ManutencaoEmCampo.Web.Tickets;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ManutencaoEmCampo.Web.Data;

public static class DemoData
{
    public static async Task InitializeAsync(IServiceProvider services, bool sqlite, string? password)
    {
        if (string.IsNullOrWhiteSpace(password))
            throw new InvalidOperationException("Defina Demo__Password para criar as contas fictícias.");

        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider;
        var db = provider.GetRequiredService<WorkDbContext>();
        if (sqlite)
            await DemoSqliteSchema.InitializeAsync(db);
        else
            await db.Database.MigrateAsync();

        var roles = provider.GetRequiredService<RoleManager<IdentityRole>>();
        foreach (var role in new[] { Roles.Supervisor, Roles.Technician })
        {
            if (!await roles.RoleExistsAsync(role))
                Check(await roles.CreateAsync(new IdentityRole(role)));
        }

        var users = provider.GetRequiredService<UserManager<WorkUser>>();
        var supervisor = await EnsureUserAsync(users, "supervisor@demo.test", "Marina Campos", Roles.Supervisor, password);
        var technician = await EnsureUserAsync(users, "tecnico@demo.test", "Rafael Lima", Roles.Technician, password);
        var other = await EnsureUserAsync(users, "tecnica@demo.test", "Camila Rocha", Roles.Technician, password);

        if (await db.Tickets.AnyAsync())
            return;

        var now = DateTime.UtcNow;
        var owner = new Actor(supervisor.Id, supervisor.DisplayName, true);
        var rafael = new Actor(technician.Id, technician.DisplayName, false);
        var pump = New("Vazamento na bomba de recirculação", "Utilidades · Bomba B-04",
            "Foi identificado um vazamento na conexão de saída. Inspecionar a vedação e registrar o serviço executado.",
            TicketPriority.High, now.AddHours(-2));
        var sensor = New("Ajustar sensor de posição", "Linha 2 · Esteira E-06",
            "O sensor apresenta leitura intermitente durante a inspeção de rotina.",
            TicketPriority.Normal, now.AddHours(-4));
        sensor.Assign(technician.Id, technician.DisplayName, owner, now.AddHours(-3));
        var fan = New("Ruído no exaustor", "Galpão B · Exaustor EX-02",
            "A operação relatou ruído acima do habitual no início do turno.",
            TicketPriority.High, now.AddHours(-5));
        fan.Assign(technician.Id, technician.DisplayName, owner, now.AddHours(-4));
        fan.Start(rafael, now.AddHours(-1));
        var motor = New("Revisar fixação do motor", "Linha 1 · Motor M-12",
            "Conferir pontos de fixação e registrar o resultado da inspeção.",
            TicketPriority.Normal, now.AddHours(-7));
        motor.Assign(other.Id, other.DisplayName, owner, now.AddHours(-6));
        var filter = New("Trocar filtro de ventilação", "Utilidades · Painel V-03",
            "Substituição preventiva conforme o checklist semanal do setor.",
            TicketPriority.Low, now.AddDays(-1));
        filter.Assign(technician.Id, technician.DisplayName, owner, now.AddDays(-1).AddMinutes(15));
        filter.Start(rafael, now.AddDays(-1).AddHours(1));
        filter.Complete(rafael, "Filtro substituído e equipamento liberado após inspeção.", now.AddDays(-1).AddHours(2));

        db.Tickets.AddRange(pump, sensor, fan, motor, filter);
        await db.SaveChangesAsync();

        Ticket New(string title, string location, string description, TicketPriority priority, DateTime createdAt) =>
            Ticket.Create(new CreateTicket
            {
                Title = title, Location = location, Description = description, Priority = priority
            }, owner, createdAt);
    }

    private static async Task<WorkUser> EnsureUserAsync(UserManager<WorkUser> users, string email,
        string name, string role, string password)
    {
        var user = await users.FindByEmailAsync(email);
        if (user is null)
        {
            user = new WorkUser { UserName = email, Email = email, DisplayName = name };
            Check(await users.CreateAsync(user, password));
        }

        if (!await users.IsInRoleAsync(user, role))
            Check(await users.AddToRoleAsync(user, role));
        return user;
    }

    private static void Check(IdentityResult result)
    {
        if (!result.Succeeded)
            throw new InvalidOperationException(string.Join(" ", result.Errors.Select(error => error.Description)));
    }
}
