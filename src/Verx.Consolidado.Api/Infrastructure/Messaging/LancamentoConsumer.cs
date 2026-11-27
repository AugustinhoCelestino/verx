using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Verx.Consolidado.Api.Application;
using Verx.Consolidado.Api.Domain;
using Verx.Contracts;

namespace Verx.Consolidado.Api.Infrastructure.Messaging;

/// <summary>Consome LancamentoRegistrado. Falhas transitórias são retentadas com atraso crescente; as persistentes vão para a DLQ (reprocessável); o broker guarda o backlog se este serviço cair.</summary>
public sealed class LancamentoConsumer(
    IServiceScopeFactory scopes,
    RabbitOptions rabbit,
    ILogger<LancamentoConsumer> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var connection = await rabbit.CreateFactory().CreateConnectionAsync(stoppingToken);
                await using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
                await RabbitTopology.DeclareAsync(channel, stoppingToken);
                await channel.BasicQosAsync(0, 50, false, stoppingToken);

                var consumer = new AsyncEventingBasicConsumer(channel);
                consumer.ReceivedAsync += (_, ea) => HandleAsync(channel, ea, stoppingToken);
                await channel.BasicConsumeAsync(RabbitTopology.Queue, autoAck: false, consumer, stoppingToken);

                while (!stoppingToken.IsCancellationRequested && connection.IsOpen)
                    await Task.Delay(TimeSpan.FromSeconds(1), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Falha na conexão com o broker; nova tentativa em 5s.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
            }
        }
    }

    private async Task HandleAsync(IChannel channel, BasicDeliverEventArgs ea, CancellationToken ct)
    {
        try
        {
            var evento = JsonSerializer.Deserialize<LancamentoRegistrado>(Encoding.UTF8.GetString(ea.Body.Span))
                         ?? throw new InvalidOperationException("Mensagem vazia.");

            using var scope = scopes.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<ConsolidadoService>();
            await service.ProcessarAsync(evento, ct);
            await channel.BasicAckAsync(ea.DeliveryTag, false, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            await DesviarAsync(channel, ea, ex, ct);
        }
    }

    /// <summary>
    /// Falha transitória: reenvia à fila de espera (atraso crescente). Falha permanente (mensagem ilegível ou
    /// rejeitada pelo domínio) ou tentativas esgotadas: vai para a DLQ, de onde o admin pode reprocessar.
    /// </summary>
    private async Task DesviarAsync(IChannel channel, BasicDeliverEventArgs ea, Exception ex, CancellationToken ct)
    {
        var tentativa = TentativaAtual(ea) + 1;
        var permanente = ex is JsonException or DomainException;
        var paraDlq = permanente || tentativa > RabbitTopology.AtrasosDeRetentativa.Length;

        var props = new BasicProperties
        {
            Persistent = true,
            ContentType = ea.BasicProperties.ContentType,
            MessageId = ea.BasicProperties.MessageId,
            Headers = new Dictionary<string, object?>
            {
                [RabbitTopology.HeaderTentativa] = tentativa,
                [RabbitTopology.HeaderErro] = ex.Message.Length > 500 ? ex.Message[..500] : ex.Message
            }
        };

        if (paraDlq)
        {
            logger.LogError(ex, "Mensagem {MessageId} enviada à DLQ após {Tentativas} tentativa(s) (permanente={Permanente}).",
                ea.BasicProperties.MessageId, tentativa, permanente);
            await channel.BasicPublishAsync(RabbitTopology.DeadLetterExchange, string.Empty, false, props, ea.Body, ct);
        }
        else
        {
            props.Headers[RabbitTopology.HeaderTentativa] = tentativa;
            logger.LogWarning(ex, "Erro ao processar mensagem {MessageId}; tentativa {Tentativa}/{Max} em {Atraso}.",
                ea.BasicProperties.MessageId, tentativa, RabbitTopology.AtrasosDeRetentativa.Length, RabbitTopology.AtrasosDeRetentativa[tentativa - 1]);
            await channel.BasicPublishAsync(string.Empty, RabbitTopology.FilaDeRetentativa(tentativa), false, props, ea.Body, ct);
        }

        // Só confirma depois de republicar: se o processo cair no meio, a mensagem volta (idempotência por EventId).
        await channel.BasicAckAsync(ea.DeliveryTag, false, ct);
    }

    private static int TentativaAtual(BasicDeliverEventArgs ea) =>
        ea.BasicProperties.Headers?.TryGetValue(RabbitTopology.HeaderTentativa, out var v) == true && v is not null
            ? Convert.ToInt32(v)
            : 0;
}
