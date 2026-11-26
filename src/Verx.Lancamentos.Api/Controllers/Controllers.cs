using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Verx.Lancamentos.Api.Application;

namespace Verx.Lancamentos.Api.Controllers;

public static class UsuarioClaims
{
    public static UsuarioAtual? Atual(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirst("sub")?.Value, out var id)
            ? new UsuarioAtual(id, user.FindFirst("name")?.Value ?? "")
            : null;
}

[ApiController]
[Route("auth")]
public sealed class AuthController(AuthService svc) : ControllerBase
{
    [HttpPost("registrar"), AllowAnonymous]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Registrar(RegistrarUsuarioRequest req, CancellationToken ct) =>
        Created("/auth/me", await svc.RegistrarAsync(req, ct));

    [HttpPost("login"), AllowAnonymous]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login(LoginRequest req, CancellationToken ct) =>
        Ok(await svc.LoginAsync(req, ct));

    [HttpGet("me"), Authorize]
    [ProducesResponseType<UsuarioResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Me(CancellationToken ct) =>
        User.Atual() is { } u && await svc.ObterAsync(u.Id, ct) is { } resp ? Ok(resp) : Unauthorized();
}

[ApiController]
[Route("lancamentos")]
[Authorize]
public sealed class LancamentosController(LancamentoService svc, TimeProvider clock, ILogger<LancamentosController> logger) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType<LancamentoResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Registrar(RegistrarLancamentoRequest req, CancellationToken ct)
    {
        if (User.Atual() is not { } usuario) return Unauthorized();
        var criado = await svc.RegistrarAsync(req, usuario, ct);
        logger.LogInformation("Lançamento {LancamentoId} registrado ({Tipo}, {Valor}).", criado.Id, criado.Tipo, criado.Valor);
        return Created($"/lancamentos/{criado.Id}", criado);
    }

    [HttpGet]
    [ProducesResponseType<IReadOnlyList<LancamentoResponse>>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Listar([FromQuery] DateOnly? data, CancellationToken ct) =>
        Ok(await svc.ListarAsync(data ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime), ct));
}
