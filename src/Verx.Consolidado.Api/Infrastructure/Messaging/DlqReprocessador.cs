using RabbitMQ.Client;
using Verx.Contracts;

namespace Verx.Consolidado.Api.Infrastructure.Messaging;

/// <summary>Operação administrativa sobre a DLQ: consulta o tamanho e devolve mensagens à fila principal.</summary>
public sealed class DlqReprocessador(RabbitOptions rabbit, ILogger<DlqReprocessador> logger)
{
    public async Task<uint> ContarAsync(CancellationToken ct)
    {
        await using var connection = await rabbit.CreateFactory().CreateConnectionAsync(ct);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: ct);
        await RabbitTopology.DeclareAsync(channel, ct);
        return (await channel.QueueDeclarePassiveAsync(RabbitTopology.DeadLetterQueue, ct)).MessageCount;
    }

    /// <summary>Move até <paramref name="limite"/> mensagens da DLQ para a fila principal, zerando as tentativas. O consumidor é idempotente por EventId.</summary>
    public async Task<int> ReprocessarAsync(int limite, CancellationToken ct)
    {
        await using var connection = await rabbit.CreateFactory().CreateConnectionAsync(ct);
        await using var channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true), ct);
        await RabbitTopology.DeclareAsync(channel, ct);

        var movidas = 0;
        while (movidas < limite && await channel.BasicGetAsync(RabbitTopology.DeadLetterQueue, autoAck: false, ct) is { } msg)
        {
            var props = new BasicProperties
            {
                Persistent = true,
                ContentType = msg.BasicProperties.ContentType,
                MessageId = msg.BasicProperties.MessageId
            };
            // Publicação confirmada antes do ack: a mensagem nunca se perde no caminho.
            await channel.BasicPublishAsync(string.Empty, RabbitTopology.Queue, false, props, msg.Body, ct);
            await channel.BasicAckAsync(msg.DeliveryTag, false, ct);
            movidas++;
        }

        logger.LogWarning("{Quantidade} mensagem(ns) reprocessada(s) da DLQ.", movidas);
        return movidas;
    }
}
