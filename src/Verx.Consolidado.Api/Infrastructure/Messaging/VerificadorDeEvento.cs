using System.Net.Http.Json;
using Verx.Contracts;

namespace Verx.Consolidado.Api.Infrastructure.Messaging;

/// <summary>Modo local (HTTP): confirma no serviço de Lançamentos que o evento recebido existe e é idêntico ao emitido.</summary>
public sealed class VerificadorDeEvento(HttpClient client, IConfiguration config)
{
    /// <exception cref="HttpRequestException">Lançamentos indisponível ou resposta inesperada (o chamador deve tentar de novo).</exception>
    public async Task<bool> EhAutenticoAsync(LancamentoRegistrado evento, CancellationToken ct)
    {
        var url = $"{(config["Messaging:Http:LancamentosUrl"] ?? "http://localhost:5101").TrimEnd('/')}/internal/eventos/{evento.EventId}";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.Add(ChaveInterna.Cabecalho, config["Messaging:Http:ApiKey"]);
        using var resp = await client.SendAsync(req, ct);

        if (resp.StatusCode == System.Net.HttpStatusCode.NotFound) return false;
        resp.EnsureSuccessStatusCode();

        var original = await resp.Content.ReadFromJsonAsync<LancamentoRegistrado>(ct);
        return original is not null
            && original.EventId == evento.EventId
            && original.LancamentoId == evento.LancamentoId
            && original.Tipo == evento.Tipo
            && original.Valor == evento.Valor
            && original.Data == evento.Data;
    }
}
