using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Logging;

namespace Verx.Observability;

public static class ObservabilityExtensions
{
    /// <summary>Registra o buffer de logs e os controllers <c>/health/*</c> e <c>/logs</c>.</summary>
    public static IServiceCollection AddObservability(this IServiceCollection services)
    {
        var store = new LogStore();
        services.AddSingleton(store);
        services.AddLogging(l => l.AddProvider(new LogStoreProvider(store)));
        services.AddControllers().AddApplicationPart(typeof(ObservabilityExtensions).Assembly);
        return services;
    }

    /// <summary>Garante um trace id por requisição e o devolve no cabeçalho <c>X-Correlation-Id</c>. Use antes dos demais middlewares.</summary>
    public static IApplicationBuilder UseCorrelacao(this IApplicationBuilder app) =>
        app.Use(async (ctx, next) =>
        {
            using var propria = System.Diagnostics.Activity.Current is null ? new System.Diagnostics.Activity("http").Start() : null;
            ctx.Response.Headers["X-Correlation-Id"] = System.Diagnostics.Activity.Current!.TraceId.ToString();
            await next();
        });
}

public sealed record HealthCheckItem(string Nome, string Status, string? Descricao, double DuracaoMs);

public sealed record HealthResponse(string Status, IReadOnlyList<HealthCheckItem> Verificacoes);

[ApiController]
[AllowAnonymous]
[Route("health")]
[Tags("Health")]
public sealed class HealthController(HealthCheckService health) : ControllerBase
{
    /// <summary>O processo está de pé (não consulta dependências).</summary>
    [HttpGet("live")]
    [ProducesResponseType<HealthResponse>(StatusCodes.Status200OK)]
    public ActionResult<HealthResponse> Live() => new HealthResponse("Healthy", []);

    /// <summary>O serviço está pronto para receber tráfego (inclui o banco de dados).</summary>
    [HttpGet("ready")]
    [ProducesResponseType<HealthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<HealthResponse>(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<HealthResponse>> Ready(CancellationToken ct)
    {
        var report = await health.CheckHealthAsync(ct);
        var resposta = new HealthResponse(report.Status.ToString(),
            report.Entries.Select(e => new HealthCheckItem(e.Key, e.Value.Status.ToString(), e.Value.Description, e.Value.Duration.TotalMilliseconds)).ToList());
        return report.Status == HealthStatus.Healthy ? resposta : StatusCode(StatusCodes.Status503ServiceUnavailable, resposta);
    }
}

[ApiController]
[Authorize(Roles = "admin")]
[Route("logs")]
[Tags("Logs")]
public sealed class LogsController(LogStore store) : ControllerBase
{
    /// <summary>Últimos logs deste serviço (em memória, mais recentes primeiro).</summary>
    /// <param name="nivel">Nível mínimo: Trace, Debug, Information, Warning, Error ou Critical.</param>
    /// <param name="busca">Texto contido na mensagem ou na categoria.</param>
    /// <param name="limite">Quantidade máxima (1 a 500).</param>
    [HttpGet]
    [ProducesResponseType<IReadOnlyList<LogEntry>>(StatusCodes.Status200OK)]
    public ActionResult<IReadOnlyList<LogEntry>> Consultar(
        [FromQuery] LogLevel nivel = LogLevel.Information, [FromQuery] string? busca = null, [FromQuery] int limite = 100) =>
        Ok(store.Consultar(nivel, busca, Math.Clamp(limite, 1, 500)));
}
