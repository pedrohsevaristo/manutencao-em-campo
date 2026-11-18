using System.Text.Encodings.Web;
using ManutencaoEmCampo.Web.Tickets;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace ManutencaoEmCampo.Web.Data;

internal sealed class ActiveBearerHandler(IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger, UrlEncoder encoder, ActiveSessions sessions)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var token = await Context.AuthenticateAsync(SessionSchemes.Token);
        if (!token.Succeeded)
            return token;

        if (!await sessions.ValidateAsync(token.Principal, Context.RequestAborted))
            return AuthenticateResult.Fail("Sessão encerrada.");

        return AuthenticateResult.Success(new AuthenticationTicket(token.Principal!, token.Properties, Scheme.Name));
    }

    protected override Task HandleChallengeAsync(AuthenticationProperties properties) =>
        Context.ChallengeAsync(SessionSchemes.Token, properties);
}
