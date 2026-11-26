using System.Security.Cryptography;
using System.Text;
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
    public async Task<IActionResult> Obter(DateOnly data, CancellationToken ct) =>
        Ok(await svc.ObterAsync(data, ct));

    [HttpGet]
    public async Task<IActionResult> ListarPeriodo([BindRequired, FromQuery] DateOnly de, [BindRequired, FromQuery] DateOnly ate, CancellationToken ct) =>
        de > ate
            ? BadRequest(new ProblemDetails { Title = "'de' deve ser anterior ou igual a 'ate'.", Status = 400 })
            : Ok(await svc.ListarPeriodoAsync(de, ate, ct));
}

/// <summary>Modo local (sem broker): recebe eventos do serviço de Lançamentos por HTTP, protegido por chave interna.</summary>
[ApiController]
[Route("internal/eventos")]
[AllowAnonymous]
[ApiExplorerSettings(IgnoreApi = true)]
public sealed class EventosInternosController(ConsolidadoService svc, IConfiguration config) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Receber(LancamentoRegistrado evento, CancellationToken ct)
    {
        if (config["Messaging:Provider"] != "Http") return NotFound();

        var chave = Encoding.UTF8.GetBytes(config["Messaging:Http:ApiKey"] ?? "dev-only-internal-key");
        var recebida = Encoding.UTF8.GetBytes(Request.Headers["X-Api-Key"].ToString());
        if (!CryptographicOperations.FixedTimeEquals(recebida, chave)) return Unauthorized();

        await svc.ProcessarAsync(evento, ct);
        return Accepted();
    }
}
