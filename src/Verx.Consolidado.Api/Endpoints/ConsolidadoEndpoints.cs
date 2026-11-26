using Verx.Consolidado.Api.Application;
using Verx.Contracts;

namespace Verx.Consolidado.Api.Endpoints;

public static class ConsolidadoEndpoints
{
    public static void MapConsolidado(this WebApplication app)
    {
        var api = app.MapGroup("/consolidado").RequireAuthorization();

        api.MapGet("/{data}", async (DateOnly data, ConsolidadoService svc, CancellationToken ct) =>
            Results.Ok(await svc.ObterAsync(data, ct)));

        api.MapGet("/", async (DateOnly de, DateOnly ate, ConsolidadoService svc, CancellationToken ct) =>
            de > ate
                ? Results.BadRequest(new Microsoft.AspNetCore.Mvc.ProblemDetails { Title = "'de' deve ser anterior ou igual a 'ate'.", Status = 400 })
                : Results.Ok(await svc.ListarPeriodoAsync(de, ate, ct)));
    }

    /// <summary>Modo local (sem broker): recebe eventos do serviço de Lançamentos por HTTP, protegido por chave interna.</summary>
    public static void MapEventosInternos(this WebApplication app)
    {
        var chave = System.Text.Encoding.UTF8.GetBytes(app.Configuration["Messaging:Http:ApiKey"] ?? "dev-only-internal-key");
        app.MapPost("/internal/eventos", async (HttpContext ctx, LancamentoRegistrado evento, ConsolidadoService svc, CancellationToken ct) =>
        {
            var recebida = System.Text.Encoding.UTF8.GetBytes(ctx.Request.Headers["X-Api-Key"].ToString());
            if (!System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(recebida, chave))
                return Results.Unauthorized();
            await svc.ProcessarAsync(evento, ct);
            return Results.Accepted();
        }).AllowAnonymous();
    }
}
