using Microsoft.EntityFrameworkCore;

namespace Verx.Lancamentos.Api;

/// <summary>
/// Publica as mensagens pendentes do outbox. Se o destino estiver indisponível,
/// o serviço de lançamentos segue aceitando requisições; as mensagens são entregues depois.
/// </summary>
public sealed class OutboxPublisher(
    IServiceScopeFactory scopes,
    IOutboxTransportFactory transports,
    TimeProvider clock,
    ILogger<OutboxPublisher> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var transport = await transports.ConnectAsync(stoppingToken);

                while (!stoppingToken.IsCancellationRequested && transport.IsOpen)
                {
                    var publicadas = await PublicarPendentesAsync(transport, stoppingToken);
                    if (publicadas == 0) await Task.Delay(PollInterval, stoppingToken);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Destino indisponível ou falha na publicação; nova tentativa em 5s.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    internal async Task<int> PublicarPendentesAsync(IOutboxTransport transport, CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LancamentosDbContext>();

        var pendentes = await db.Outbox
            .Where(m => m.PublicadoEm == null)
            .OrderBy(m => m.CriadoEm)
            .Take(100)
            .ToListAsync(ct);

        foreach (var msg in pendentes)
        {
            msg.Tentativas++;
            await transport.SendAsync(msg, ct);
            msg.PublicadoEm = clock.GetUtcNow();
            // Entrega at-least-once: se o save falhar, a mensagem é reenviada e o consumidor deduplica por EventId.
            await db.SaveChangesAsync(ct);
        }

        return pendentes.Count;
    }
}
