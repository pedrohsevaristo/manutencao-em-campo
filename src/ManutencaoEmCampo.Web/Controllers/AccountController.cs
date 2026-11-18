using ManutencaoEmCampo.Web.Data;
using ManutencaoEmCampo.Web.Tickets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ManutencaoEmCampo.Web.Controllers;

[AutoValidateAntiforgeryToken]
public sealed class AccountController(UserManager<WorkUser> users, SignInManager<WorkUser> signIn,
    ActiveSessions sessions, LoginAttempts attempts, SecurityEvents events) : Controller
{
    [HttpGet("/entrar"), AllowAnonymous]
    public IActionResult Login() => User.IsInRole(Roles.Supervisor) ? RedirectToAction("Index", "WorkOrders") : View();

    [HttpPost("/entrar"), AllowAnonymous, EnableRateLimiting(TrafficPolicy.Login)]
    public async Task<IActionResult> Login(LoginInput input, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return View(input);

        var email = input.Email.Trim();
        using var allowance = attempts.Acquire(HttpContext, users.NormalizeEmail(email)!);
        if (!allowance.IsAcquired)
        {
            events.Record(SecurityEvent.LoginBlocked, "mvc");
            TrafficPolicy.RetryAfter(Response, allowance);
            Response.StatusCode = StatusCodes.Status429TooManyRequests;
            ModelState.AddModelError("", "Muitas tentativas. Aguarde antes de tentar novamente.");
            return View(input);
        }

        var user = await users.FindByEmailAsync(email);
        var result = user is { IsActive: true }
            ? await signIn.CheckPasswordSignInAsync(user, input.Password, lockoutOnFailure: false)
            : Microsoft.AspNetCore.Identity.SignInResult.Failed;
        if (user is null || !result.Succeeded)
        {
            events.Record(result.IsLockedOut ? SecurityEvent.LoginBlocked : SecurityEvent.AuthenticationFailed, "mvc");
            ModelState.AddModelError("", "E-mail ou senha inválidos.");
            return View(input);
        }

        if (!await users.IsInRoleAsync(user, Roles.Supervisor))
        {
            events.Record(SecurityEvent.AuthorizationDenied, "mvc");
            ModelState.AddModelError("", "Use uma conta de supervisão para acessar o painel.");
            return View(input);
        }

        var session = await sessions.CreateAsync(user, ActiveSessions.CookieLifetime, cancellationToken);
        await signIn.SignInWithClaimsAsync(user, isPersistent: false, [session]);
        events.Record(SecurityEvent.AuthenticationSucceeded, "mvc");
        return RedirectToAction("Index", "WorkOrders");
    }

    [HttpPost("/sair"), Authorize]
    public async Task<IActionResult> Logout(CancellationToken cancellationToken)
    {
        await sessions.RevokeAsync(User, cancellationToken);
        await signIn.SignOutAsync();
        return RedirectToAction(nameof(Login));
    }

    [HttpGet("/acesso-negado")]
    public IActionResult Denied() => View();
}
