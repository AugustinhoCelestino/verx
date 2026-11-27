using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Verx.Integration.Tests;

public sealed class IntegracaoEntreServicosTests(Ambiente a) : IClassFixture<Ambiente>
{
    private static int _dia;

    // Cada teste usa um dia próprio: o banco é compartilhado entre os testes da classe.
    private static string NovoDia() => new DateOnly(2031, 1, 1).AddDays(Interlocked.Increment(ref _dia)).ToString("yyyy-MM-dd");

    private async Task<Guid> RegistrarAsync(string token, int tipo, decimal valor, string dia)
    {
        var r = await Ambiente.Enviar(HttpMethod.Post, a.Lancamentos, "/lancamentos", token, new { tipo, valor, data = dia, descricao = "teste" });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("id").GetGuid();
    }

    private async Task<JsonElement> SaldoAsync(string token, string dia)
    {
        var r = await Ambiente.Enviar(HttpMethod.Get, a.Consolidado, $"/consolidado/{dia}", token);
        r.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    private async Task<JsonElement> AguardarLancamentosAsync(string token, string dia, int quantidade)
    {
        var limite = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < limite)
        {
            var saldo = await SaldoAsync(token, dia);
            if (saldo.GetProperty("quantidadeLancamentos").GetInt32() >= quantidade) return saldo;
            await Task.Delay(250);
        }
        throw new TimeoutException($"O consolidado de {dia} não chegou a {quantidade} lançamento(s).");
    }

    [Fact]
    public async Task Lancamentos_registrados_chegam_ao_saldo_do_consolidado()
    {
        var token = await a.LoginAdminAsync();
        var dia = NovoDia();

        await RegistrarAsync(token, 1, 100m, dia);
        await RegistrarAsync(token, 2, 30.50m, dia);

        var saldo = await AguardarLancamentosAsync(token, dia, 2);
        Assert.Equal(100m, saldo.GetProperty("totalCreditos").GetDecimal());
        Assert.Equal(30.50m, saldo.GetProperty("totalDebitos").GetDecimal());
        Assert.Equal(69.50m, saldo.GetProperty("saldo").GetDecimal());
    }

    [Fact]
    public async Task Evento_sem_lancamento_correspondente_nao_altera_o_saldo()
    {
        var token = await a.LoginAdminAsync();
        var dia = NovoDia();
        var forjado = new { eventId = Guid.NewGuid(), lancamentoId = Guid.NewGuid(), tipo = 1, valor = 1_000_000m, data = dia, ocorridoEm = DateTimeOffset.UtcNow };

        var r = await Enviar(forjado);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, r.StatusCode);
        Assert.Equal(0, (await SaldoAsync(token, dia)).GetProperty("quantidadeLancamentos").GetInt32());
    }

    [Fact]
    public async Task Evento_adulterado_de_lancamento_real_e_recusado_e_o_original_e_idempotente()
    {
        var token = await a.LoginAdminAsync();
        var dia = NovoDia();
        var lancamentoId = await RegistrarAsync(token, 1, 50m, dia);
        await AguardarLancamentosAsync(token, dia, 1);

        using var original = await a.LerEventoDoOutboxAsync(lancamentoId);
        var raiz = original.RootElement;
        var eventId = raiz.GetProperty("eventId").GetGuid();

        var adulterado = new { eventId, lancamentoId, tipo = 1, valor = 5000m, data = dia, ocorridoEm = DateTimeOffset.UtcNow };
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Enviar(adulterado)).StatusCode);

        var novoIdMesmoLancamento = new { eventId = Guid.NewGuid(), lancamentoId, tipo = 1, valor = 50m, data = dia, ocorridoEm = DateTimeOffset.UtcNow };
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await Enviar(novoIdMesmoLancamento)).StatusCode);

        var reenvio = new { eventId, lancamentoId, tipo = 1, valor = 50m, data = dia, ocorridoEm = raiz.GetProperty("ocorridoEm").GetDateTimeOffset() };
        Assert.Equal(HttpStatusCode.Accepted, (await Enviar(reenvio)).StatusCode);

        var saldo = await SaldoAsync(token, dia);
        Assert.Equal(1, saldo.GetProperty("quantidadeLancamentos").GetInt32());
        Assert.Equal(50m, saldo.GetProperty("saldo").GetDecimal());
    }

    private Task<HttpResponseMessage> Enviar(object evento)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/internal/eventos") { Content = JsonContent.Create(evento) };
        req.Headers.Add("X-Api-Key", Ambiente.ChaveInterna);
        return a.Consolidado.SendAsync(req);
    }
}
