using System.ComponentModel.DataAnnotations;
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
public sealed class SessionApiController(UserManager<WorkUser> users, SignInManager<WorkUser> signIn) : ControllerBase
{
    [HttpPost, AllowAnonymous, EnableRateLimiting("login")]
    public async Task<IActionResult> Login(LoginInput input)
    {
        var user = await users.FindByEmailAsync(input.Email.Trim());
        if (user is null || !(await signIn.CheckPasswordSignInAsync(user, input.Password, lockoutOnFailure: true)).Succeeded)
            return Problem(statusCode: 401, title: "E-mail ou senha inválidos.");

        return SignIn(await signIn.CreateUserPrincipalAsync(user), SessionSchemes.Api);
    }

    [HttpGet, Authorize(AuthenticationSchemes = SessionSchemes.Api, Roles = Roles.Operators)]
    public IActionResult Current()
    {
        var actor = Actor.From(User);
        return Ok(new { actor.Name, Role = actor.IsSupervisor ? Roles.Supervisor : Roles.Technician });
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
