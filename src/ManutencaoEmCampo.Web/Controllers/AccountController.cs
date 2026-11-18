using ManutencaoEmCampo.Web.Data;
using ManutencaoEmCampo.Web.Tickets;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ManutencaoEmCampo.Web.Controllers;

[AutoValidateAntiforgeryToken]
public sealed class AccountController(UserManager<WorkUser> users, SignInManager<WorkUser> signIn) : Controller
{
    [HttpGet("/entrar"), AllowAnonymous]
    public IActionResult Login() => User.IsInRole(Roles.Supervisor) ? RedirectToAction("Index", "WorkOrders") : View();

    [HttpPost("/entrar"), AllowAnonymous, EnableRateLimiting("login")]
    public async Task<IActionResult> Login(LoginInput input)
    {
        if (!ModelState.IsValid)
            return View(input);

        var user = await users.FindByEmailAsync(input.Email.Trim());
        if (user is null || !(await signIn.CheckPasswordSignInAsync(user, input.Password, lockoutOnFailure: true)).Succeeded)
        {
            ModelState.AddModelError("", "E-mail ou senha inválidos.");
            return View(input);
        }

        if (!await users.IsInRoleAsync(user, Roles.Supervisor))
        {
            ModelState.AddModelError("", "Use uma conta de supervisão para acessar o painel.");
            return View(input);
        }

        await signIn.SignInAsync(user, isPersistent: false);
        return RedirectToAction("Index", "WorkOrders");
    }

    [HttpPost("/sair"), Authorize]
    public async Task<IActionResult> Logout()
    {
        await signIn.SignOutAsync();
        return RedirectToAction(nameof(Login));
    }

    [HttpGet("/acesso-negado")]
    public IActionResult Denied() => View();
}
