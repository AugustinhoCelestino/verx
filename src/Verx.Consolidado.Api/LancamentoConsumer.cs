using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using Verx.Contracts;

namespace Verx.Consolidado.Api;

/// <summary>Consome LancamentoRegistrado. Falhas persistentes vão para a DLQ; o broker guarda o backlog se este serviço cair.</summary>
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
            // Redelivered=true indica segunda falha: descarta para a DLQ; senão, uma nova tentativa.
            logger.LogError(ex, "Erro ao processar mensagem {Tag} (redelivered={Redelivered}).", ea.DeliveryTag, ea.Redelivered);
            await channel.BasicNackAsync(ea.DeliveryTag, false, requeue: !ea.Redelivered, ct);
        }
    }
}
