using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading.Tasks;

namespace ManutencaoEmCampo.Legacy;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        var demo = args.Length > 0 && args[0] == "--demo";
        var addressIndex = demo ? 1 : 0;
        var address = args.Length > addressIndex ? args[addressIndex] : demo ? "http://localhost:5180" : null;
        if (args.Length > addressIndex + 1 || !Uri.TryCreate(address, UriKind.Absolute, out var origin)
            || !string.IsNullOrEmpty(origin.UserInfo)
            || (origin.Scheme != Uri.UriSchemeHttps && !(demo && origin.Scheme == Uri.UriSchemeHttp && origin.IsLoopback)))
        {
            Console.Error.WriteLine("Informe o endereço HTTPS da API ou use --demo para a demonstração local.");
            return 1;
        }

        var email = Environment.GetEnvironmentVariable("LEGACY_EMAIL");
        if (string.IsNullOrWhiteSpace(email))
        {
            Console.Write("E-mail: ");
            email = Console.ReadLine()?.Trim();
        }
        var password = Environment.GetEnvironmentVariable("DEMO_PASSWORD") ?? ReadPassword();
        if (email is null || password is null || string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            Console.Error.WriteLine("Informe o e-mail e a senha da conta.");
            return 1;
        }

        try
        {
            using var handler = new HttpClientHandler { AllowAutoRedirect = false };
            using var client = new HttpClient(handler) { BaseAddress = origin, Timeout = TimeSpan.FromSeconds(10) };
            var login = new Login { Email = email, Password = password };
            using var content = new ByteArrayContent(Serialize(login));
            content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
            using var response = await client.PostAsync("/api/session", content);
            var session = await ReadAsync<Session>(response);
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.AccessToken);

            TicketPage page;
            try
            {
                using var ticketsResponse = await client.GetAsync("/api/chamados?pageSize=10");
                page = await ReadAsync<TicketPage>(ticketsResponse);
            }
            finally
            {
                await EndSessionAsync(client);
            }

            Console.WriteLine("Manutenção em Campo | Consulta .NET Framework 4.8");
            Console.WriteLine($"Exibindo {page.Items.Count} de {page.Total} chamados da conta.");
            foreach (var ticket in page.Items)
                Console.WriteLine($"#{ticket.Id}  {Label(ticket.Status),-16}  {ticket.Title}");
            return 0;
        }
        catch (HttpRequestException error)
        {
            Console.Error.WriteLine($"Não foi possível consultar a API. {error.Message}");
            return 1;
        }
        catch (TaskCanceledException)
        {
            Console.Error.WriteLine("A API não respondeu dentro do tempo de espera.");
            return 1;
        }
        catch (SerializationException)
        {
            Console.Error.WriteLine("A API retornou uma resposta em formato inesperado.");
            return 1;
        }
    }

    private static async Task EndSessionAsync(HttpClient client)
    {
        try
        {
            using var response = await client.DeleteAsync("/api/session");
            if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.Unauthorized)
                return;
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException)
        {
            // The server also expires sessions that cannot be revoked when the client exits.
        }
        Console.Error.WriteLine("A API não confirmou o encerramento da sessão.");
    }

    private static string Label(string status) => status switch
    {
        "Open" => "Aberto",
        "InProgress" => "Em atendimento",
        "Completed" => "Concluído",
        _ => status
    };

    private static string? ReadPassword()
    {
        Console.Write("Senha: ");
        if (Console.IsInputRedirected)
            return Console.ReadLine();

        var value = new StringBuilder();
        ConsoleKeyInfo key;
        while ((key = Console.ReadKey(intercept: true)).Key != ConsoleKey.Enter)
        {
            if (key.Key == ConsoleKey.Backspace && value.Length > 0)
                value.Length--;
            else if (!char.IsControl(key.KeyChar))
                value.Append(key.KeyChar);
        }
        Console.WriteLine();
        return value.ToString();
    }

    private static byte[] Serialize<T>(T data)
    {
        using var stream = new MemoryStream();
        new DataContractJsonSerializer(typeof(T)).WriteObject(stream, data);
        return stream.ToArray();
    }

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        response.EnsureSuccessStatusCode();
        using var stream = await response.Content.ReadAsStreamAsync();
        return (T)new DataContractJsonSerializer(typeof(T)).ReadObject(stream);
    }
}

[DataContract]
internal sealed class Login
{
    [DataMember(Name = "email")] public string Email { get; set; } = "";
    [DataMember(Name = "password")] public string Password { get; set; } = "";
}

[DataContract]
internal sealed class Session
{
    [DataMember(Name = "accessToken", IsRequired = true)] public string AccessToken { get; set; } = "";
}

[DataContract]
internal sealed class TicketPage
{
    [DataMember(Name = "items", IsRequired = true)] public List<TicketSummary> Items { get; set; } = new();
    [DataMember(Name = "total", IsRequired = true)] public int Total { get; set; }
}

[DataContract]
internal sealed class TicketSummary
{
    [DataMember(Name = "id")] public int Id { get; set; }
    [DataMember(Name = "title")] public string Title { get; set; } = "";
    [DataMember(Name = "status")] public string Status { get; set; } = "";
}
