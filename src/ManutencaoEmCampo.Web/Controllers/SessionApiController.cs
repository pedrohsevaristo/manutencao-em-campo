using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using ManutencaoEmCampo.Web.Data;
using ManutencaoEmCampo.Web.Tickets;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ManutencaoEmCampo.Web.Controllers;

[ApiController]
[Route("api/session")]
public sealed class SessionApiController(UserManager<WorkUser> users, SignInManager<WorkUser> signIn,
    ActiveSessions sessions, LoginAttempts attempts, SecurityEvents events) : ControllerBase
{
    [HttpPost, AllowAnonymous, EnableRateLimiting(TrafficPolicy.Login)]
    public async Task<IActionResult> Login(LoginInput input, CancellationToken cancellationToken)
    {
        var email = input.Email.Trim();
        using var allowance = attempts.Acquire(HttpContext, users.NormalizeEmail(email)!);
        if (!allowance.IsAcquired)
        {
            events.Record(SecurityEvent.LoginBlocked, "api");
            TrafficPolicy.RetryAfter(Response, allowance);
            return Problem(statusCode: 429, title: "Muitas tentativas. Aguarde antes de tentar novamente.");
        }

        var user = await users.FindByEmailAsync(email);
        var result = user is { IsActive: true }
            ? await signIn.CheckPasswordSignInAsync(user, input.Password, lockoutOnFailure: false)
            : Microsoft.AspNetCore.Identity.SignInResult.Failed;
        if (user is null || !result.Succeeded)
        {
            events.Record(result.IsLockedOut ? SecurityEvent.LoginBlocked : SecurityEvent.AuthenticationFailed, "api");
            return Problem(statusCode: 401, title: "E-mail ou senha inválidos.");
        }

        var principal = await signIn.CreateUserPrincipalAsync(user);
        if (!principal.IsInRole(Roles.Supervisor) && !principal.IsInRole(Roles.Technician))
        {
            events.Record(SecurityEvent.AuthorizationDenied, "api");
            return Problem(statusCode: 403, title: "Conta sem perfil operacional.");
        }

        var session = await sessions.CreateAsync(user, ActiveSessions.BearerLifetime, cancellationToken);
        ((ClaimsIdentity)principal.Identity!).AddClaim(session);
        events.Record(SecurityEvent.AuthenticationSucceeded, "api");
        return SignIn(principal, SessionSchemes.Token);
    }

    [HttpGet, Authorize(AuthenticationSchemes = SessionSchemes.Api, Roles = Roles.Operators), EnableRateLimiting(TrafficPolicy.Operations)]
    public IActionResult Current()
    {
        var actor = Actor.From(User);
        return Ok(new { actor.Name, Role = actor.IsSupervisor ? Roles.Supervisor : Roles.Technician });
    }

    [HttpDelete, Authorize(AuthenticationSchemes = SessionSchemes.Api, Roles = Roles.Operators)]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        await sessions.RevokeAsync(User, cancellationToken);
        return NoContent();
    }
}

public sealed class LoginInput
{
    [Required(ErrorMessage = "Informe seu e-mail."), EmailAddress(ErrorMessage = "Informe um e-mail válido."), StringLength(254)]
    [Display(Name = "E-mail")]
    public string Email { get; init; } = "";

    [Required(ErrorMessage = "Informe sua senha."), DataType(DataType.Password), StringLength(128)]
    [Display(Name = "Senha")]
    public string Password { get; init; } = "";
}
