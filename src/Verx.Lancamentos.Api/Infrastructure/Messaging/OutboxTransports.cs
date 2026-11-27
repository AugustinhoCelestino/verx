using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using Verx.Contracts;

using Verx.Lancamentos.Api.Infrastructure.Persistence;

namespace Verx.Lancamentos.Api.Infrastructure.Messaging;

/// <summary>Canal de saída do outbox. Implementações: RabbitMQ (padrão) ou HTTP (modo local sem broker).</summary>
public interface IOutboxTransport : IAsyncDisposable
{
    bool IsOpen { get; }
    Task SendAsync(OutboxMessage message, CancellationToken ct);
}

/// <summary>O destino está acessível mas recusou a mensagem (ex.: 4xx/500): conta como tentativa daquela mensagem, não como indisponibilidade.</summary>
public sealed class MensagemRecusadaException(string message) : Exception(message);

public interface IOutboxTransportFactory
{
    Task<IOutboxTransport> ConnectAsync(CancellationToken ct);
}

public sealed class RabbitTransportFactory(RabbitOptions options) : IOutboxTransportFactory
{
    public async Task<IOutboxTransport> ConnectAsync(CancellationToken ct)
    {
        var connection = await options.CreateFactory().CreateConnectionAsync(ct);
        var channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
            ct);
        await RabbitTopology.DeclareAsync(channel, ct);
        return new RabbitTransport(connection, channel);
    }

    private sealed class RabbitTransport(IConnection connection, IChannel channel) : IOutboxTransport
    {
        public bool IsOpen => connection.IsOpen;

        public async Task SendAsync(OutboxMessage message, CancellationToken ct)
        {
            var props = new BasicProperties
            {
                Persistent = true,
                MessageId = message.Id.ToString(),
                ContentType = "application/json"
            };
            await channel.BasicPublishAsync(RabbitTopology.Exchange, RabbitTopology.RoutingKey, false, props,
                Encoding.UTF8.GetBytes(message.Payload), ct);
        }

        public async ValueTask DisposeAsync()
        {
            await channel.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}

public sealed class HttpTransportOptions
{
    public string ConsolidadoUrl { get; set; } = "http://localhost:5102";
    public string ApiKey { get; set; } = "";
}

/// <summary>Modo local: entrega o evento por HTTP ao Consolidado. O outbox garante retentativa se ele estiver fora.</summary>
public sealed class HttpTransportFactory(IHttpClientFactory clients, IOptions<HttpTransportOptions> options) : IOutboxTransportFactory
{
    public Task<IOutboxTransport> ConnectAsync(CancellationToken ct)
    {
        var client = clients.CreateClient("consolidado");
        client.BaseAddress = new Uri(options.Value.ConsolidadoUrl);
        client.DefaultRequestHeaders.Remove(ChaveInterna.Cabecalho);
                client.DefaultRequestHeaders.Add(ChaveInterna.Cabecalho, options.Value.ApiKey);
        return Task.FromResult<IOutboxTransport>(new HttpTransport(client));
    }

    private sealed class HttpTransport(HttpClient client) : IOutboxTransport
    {
        public bool IsOpen => true;

        public async Task SendAsync(OutboxMessage message, CancellationToken ct)
        {
            using var content = new StringContent(message.Payload, Encoding.UTF8, "application/json");
            using var response = await client.PostAsync("/internal/eventos", content, ct);
            if (response.IsSuccessStatusCode) return;

            // 502/503/504 e 429 indicam indisponibilidade temporária: o publisher tenta de novo sem consumir tentativas.
            if (response.StatusCode is HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable
                or HttpStatusCode.GatewayTimeout or HttpStatusCode.TooManyRequests or HttpStatusCode.RequestTimeout)
                response.EnsureSuccessStatusCode();

            throw new MensagemRecusadaException($"Consolidado respondeu {(int)response.StatusCode} {response.StatusCode}.");
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
