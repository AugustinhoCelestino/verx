using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Verx.Contracts;
using Verx.Lancamentos.Api.Application;
using Verx.Lancamentos.Api.Infrastructure.Persistence;

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
public sealed class AuthController(AuthService svc, IConfiguration config) : ControllerBase
{
    /// <summary>Cadastro fechado por padrão: só um admin cria contas, salvo <c>Auth:PermitirAutoCadastro=true</c> (demonstração).</summary>
    [HttpPost("registrar"), AllowAnonymous]
    [ProducesResponseType<LoginResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> Registrar(RegistrarUsuarioRequest req, CancellationToken ct)
    {
        if (!config.GetValue("Auth:PermitirAutoCadastro", false) && !User.IsInRole("admin"))
        {
            var autenticado = User.Identity?.IsAuthenticated == true;
            var status = autenticado ? StatusCodes.Status403Forbidden : StatusCodes.Status401Unauthorized;
            return StatusCode(status, new ProblemDetails { Title = "O cadastro de usuários é restrito a administradores.", Status = status });
        }
        return Created("/auth/me", await svc.RegistrarAsync(req, ct));
    }

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

/// <summary>Modo local (HTTP): o Consolidado confere aqui se o evento recebido foi realmente emitido por um lançamento.</summary>
[ApiController]
[Route("internal/eventos")]
[AllowAnonymous]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class EventosInternosController(LancamentosDbContext db, IConfiguration config) : ControllerBase
{
    [HttpGet("{eventId:guid}")]
    public async Task<IActionResult> Obter(Guid eventId, CancellationToken ct)
    {
        if (config["Messaging:Provider"] != "Http") return NotFound();
        if (!ChaveInterna.Confere(Request.Headers[ChaveInterna.Cabecalho].ToString(), config["Messaging:Http:ApiKey"])) return Unauthorized();

        var payload = await db.Outbox.AsNoTracking().Where(m => m.Id == eventId).Select(m => m.Payload).FirstOrDefaultAsync(ct);
        return payload is null ? NotFound() : Content(payload, "application/json");
    }
}

