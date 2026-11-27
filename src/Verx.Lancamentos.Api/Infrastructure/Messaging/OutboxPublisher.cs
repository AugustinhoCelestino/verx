using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;

using Verx.Lancamentos.Api.Infrastructure.Persistence;

namespace Verx.Lancamentos.Api.Infrastructure.Messaging;

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
    private const int MaxTentativas = 5;

    public const string MeterName = "Verx.Lancamentos";
    private static readonly Meter Metricas = new(MeterName);
    private static readonly Counter<long> Descartadas = Metricas.CreateCounter<long>("verx_outbox_descartadas_total");

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
            .Where(m => m.PublicadoEm == null && m.DescartadoEm == null)
            .OrderBy(m => m.CriadoEm)
            .Take(100)
            .ToListAsync(ct);

        var publicadas = 0;
        foreach (var msg in pendentes)
        {
            try
            {
                await transport.SendAsync(msg, ct);
                msg.PublicadoEm = clock.GetUtcNow();
                publicadas++;
            }
            catch (MensagemRecusadaException ex)
            {
                // Mensagem "venenosa": não pode travar as seguintes. Após MaxTentativas é desviada para análise.
                msg.Tentativas++;
                msg.UltimoErro = ex.Message;
                if (msg.Tentativas >= MaxTentativas)
                {
                    msg.DescartadoEm = clock.GetUtcNow();
                    Descartadas.Add(1);
                    logger.LogError(ex, "Outbox {MensagemId} descartada após {Tentativas} tentativas; requer análise manual. Saldo pode estar divergente.",
                        msg.Id, msg.Tentativas);
                }
                else
                {
                    logger.LogWarning(ex, "Outbox {MensagemId} recusada (tentativa {Tentativas}/{Max}).", msg.Id, msg.Tentativas, MaxTentativas);
                }
            }

            // Entrega at-least-once: se o save falhar, a mensagem é reenviada e o consumidor deduplica por EventId.
            await db.SaveChangesAsync(ct);
        }

        return publicadas;
    }
}
