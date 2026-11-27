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
    public const string RetryQueuePrefix = "consolidado.lancamento-registrado.retry.";
    public const string HeaderTentativa = "x-tentativa";
    public const string HeaderErro = "x-erro";

    /// <summary>Intervalo antes de cada nova tentativa (backoff crescente). Esgotado, a mensagem vai para a DLQ.</summary>
    public static readonly TimeSpan[] AtrasosDeRetentativa = [TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(5)];

    public static string FilaDeRetentativa(int tentativa) => $"{RetryQueuePrefix}{(int)AtrasosDeRetentativa[tentativa - 1].TotalSeconds}s";

    public static async Task DeclareAsync(IChannel channel, CancellationToken ct = default)
    {
        await channel.ExchangeDeclareAsync(DeadLetterExchange, ExchangeType.Fanout, durable: true, cancellationToken: ct);
        await channel.QueueDeclareAsync(DeadLetterQueue, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);
        await channel.QueueBindAsync(DeadLetterQueue, DeadLetterExchange, string.Empty, cancellationToken: ct);

        await channel.ExchangeDeclareAsync(Exchange, ExchangeType.Topic, durable: true, cancellationToken: ct);
        var args = new Dictionary<string, object?> { ["x-dead-letter-exchange"] = DeadLetterExchange };
        await channel.QueueDeclareAsync(Queue, durable: true, exclusive: false, autoDelete: false, arguments: args, cancellationToken: ct);
        await channel.QueueBindAsync(Queue, Exchange, RoutingKey, cancellationToken: ct);

        // Filas de espera: a mensagem expira após o atraso e volta sozinha à fila principal (exchange padrão).
        for (var tentativa = 1; tentativa <= AtrasosDeRetentativa.Length; tentativa++)
        {
            var retryArgs = new Dictionary<string, object?>
            {
                ["x-message-ttl"] = (int)AtrasosDeRetentativa[tentativa - 1].TotalMilliseconds,
                ["x-dead-letter-exchange"] = string.Empty,
                ["x-dead-letter-routing-key"] = Queue
            };
            await channel.QueueDeclareAsync(FilaDeRetentativa(tentativa), durable: true, exclusive: false, autoDelete: false, arguments: retryArgs, cancellationToken: ct);
        }
    }
}

public sealed class RabbitOptions
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string User { get; set; } = "";
    public string Password { get; set; } = "";

    public ConnectionFactory CreateFactory() => new()
    {
        HostName = Host,
        Port = Port,
        UserName = User,
        Password = Password,
        AutomaticRecoveryEnabled = true
    };
}
