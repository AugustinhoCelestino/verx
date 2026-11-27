using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Verx.Consolidado.Api.Application;
using Verx.Contracts;

namespace Verx.Consolidado.Api.Controllers;

[ApiController]
[Route("consolidado")]
[Authorize]
public sealed class ConsolidadoController(ConsolidadoService svc) : ControllerBase
{
    [HttpGet("{data}")]
    [ProducesResponseType<SaldoDiarioResponse>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Obter(DateOnly data, CancellationToken ct) =>
        Ok(await svc.ObterAsync(data, ct));

    [HttpGet]
    [ProducesResponseType<IReadOnlyList<SaldoDiarioResponse>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ListarPeriodo([BindRequired, FromQuery] DateOnly de, [BindRequired, FromQuery] DateOnly ate, CancellationToken ct) =>
        de > ate
            ? BadRequest(new ProblemDetails { Title = "'de' deve ser anterior ou igual a 'ate'.", Status = 400 })
            : Ok(await svc.ListarPeriodoAsync(de, ate, ct));
}

/// <summary>Administração da DLQ (só no desenho com broker; exige perfil admin).</summary>
[ApiController]
[Route("admin/dlq")]
[Authorize(Roles = "admin")]
public sealed class DlqController(DlqReprocessador? dlq = null) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Contar(CancellationToken ct) =>
        dlq is null ? NotFound() : Ok(new { mensagens = await dlq.ContarAsync(ct) });

    [HttpPost("reprocessar")]
    public async Task<IActionResult> Reprocessar([FromQuery] int limite = 100, CancellationToken ct = default) =>
        dlq is null ? NotFound() : Ok(new { reprocessadas = await dlq.ReprocessarAsync(Math.Clamp(limite, 1, 1000), ct) });
}

/// <summary>Modo local (sem broker): recebe eventos do serviço de Lançamentos por HTTP, protegido por chave interna.</summary>
[ApiController]
[Route("internal/eventos")]
[AllowAnonymous]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class EventosInternosController(ConsolidadoService svc, VerificadorDeEvento verificador, IConfiguration config) : ControllerBase
{
    [HttpPost]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Receber(LancamentoRegistrado evento, CancellationToken ct)
    {
        if (config["Messaging:Provider"] != "Http") return NotFound();
        if (!ChaveInterna.Confere(Request.Headers[ChaveInterna.Cabecalho].ToString(), config["Messaging:Http:ApiKey"])) return Unauthorized();

        try
        {
            if (!await verificador.EhAutenticoAsync(evento, ct))
                return UnprocessableEntity(new ProblemDetails { Title = "Evento sem lançamento correspondente.", Status = 422 });
        }
        catch (HttpRequestException)
        {
            // Lançamentos fora do ar: o outbox tenta de novo sem descartar a mensagem.
            return StatusCode(StatusCodes.Status503ServiceUnavailable);
        }

        await svc.ProcessarAsync(evento, ct);
        return Accepted();
    }
}
