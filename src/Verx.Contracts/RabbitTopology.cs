using RabbitMQ.Client;

namespace Verx.Contracts;

/// <summary>
/// Topologia compartilhada. Ambos os serviços a declaram (idempotente), de modo que a fila durável
/// existe mesmo que o Consolidado esteja fora do ar: nenhum evento é perdido.
/// </summary>
public static class RabbitTopology
{
    public const string Exchange = "verx.lancamentos";
    public const string RoutingKey = "lancamento.registrado";
    public const string Queue = "consolidado.lancamento-registrado";
    public const string DeadLetterExchange = "verx.lancamentos.dlx";
    public const string DeadLetterQueue = "consolidado.lancamento-registrado.dlq";

    public static async Task DeclareAsync(IChannel channel, CancellationToken ct = default)
    {
        await channel.ExchangeDeclareAsync(DeadLetterExchange, ExchangeType.Fanout, durable: true, cancellationToken: ct);
        await channel.QueueDeclareAsync(DeadLetterQueue, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);
        await channel.QueueBindAsync(DeadLetterQueue, DeadLetterExchange, string.Empty, cancellationToken: ct);

        await channel.ExchangeDeclareAsync(Exchange, ExchangeType.Topic, durable: true, cancellationToken: ct);
        var args = new Dictionary<string, object?> { ["x-dead-letter-exchange"] = DeadLetterExchange };
        await channel.QueueDeclareAsync(Queue, durable: true, exclusive: false, autoDelete: false, arguments: args, cancellationToken: ct);
        await channel.QueueBindAsync(Queue, Exchange, RoutingKey, cancellationToken: ct);
    }
}

public sealed class RabbitOptions
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string User { get; set; } = "guest";
    public string Password { get; set; } = "guest";

    public ConnectionFactory CreateFactory() => new()
    {
        HostName = Host,
        Port = Port,
        UserName = User,
        Password = Password,
        AutomaticRecoveryEnabled = true
    };
}
