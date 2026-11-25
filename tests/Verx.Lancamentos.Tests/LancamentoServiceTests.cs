using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Verx.Contracts;
using Verx.Lancamentos.Api;

namespace Verx.Lancamentos.Tests;

public sealed class LancamentoServiceTests : IAsyncLifetime
{
    private readonly SqliteConnection _conn = new("DataSource=:memory:");
    private LancamentosDbContext _db = null!;
    private readonly DateTimeOffset _now = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);

    public async Task InitializeAsync()
    {
        await _conn.OpenAsync();
        _db = new LancamentosDbContext(new DbContextOptionsBuilder<LancamentosDbContext>().UseSqlite(_conn).Options);
        await _db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _conn.DisposeAsync();
    }

    private LancamentoService Service() => new(_db, new FixedClock(_now));

    [Fact]
    public async Task Registrar_grava_lancamento_e_evento_no_outbox_juntos()
    {
        var resp = await Service().RegistrarAsync(new(TipoLancamento.Credito, 150.50m, null, "venda"), default);

        Assert.Equal(new DateOnly(2026, 11, 24), resp.Data);
        var msg = Assert.Single(_db.Outbox.ToList());
        Assert.Null(msg.PublicadoEm);
        var evento = JsonSerializer.Deserialize<LancamentoRegistrado>(msg.Payload)!;
        Assert.Equal(resp.Id, evento.LancamentoId);
        Assert.Equal(150.50m, evento.Valor);
        Assert.Equal(TipoLancamento.Credito, evento.Tipo);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(10.123)]
    public async Task Registrar_rejeita_valor_invalido_e_nao_persiste_nada(double valor)
    {
        await Assert.ThrowsAsync<DomainException>(() =>
            Service().RegistrarAsync(new(TipoLancamento.Debito, (decimal)valor, null, null), default));

        Assert.Empty(_db.Lancamentos);
        Assert.Empty(_db.Outbox);
    }

    [Fact]
    public async Task Registrar_rejeita_tipo_invalido()
    {
        await Assert.ThrowsAsync<DomainException>(() =>
            Service().RegistrarAsync(new((TipoLancamento)99, 10m, null, null), default));
    }

    [Fact]
    public async Task Listar_retorna_apenas_lancamentos_da_data()
    {
        var svc = Service();
        await svc.RegistrarAsync(new(TipoLancamento.Credito, 10m, new DateOnly(2026, 11, 1), null), default);
        await svc.RegistrarAsync(new(TipoLancamento.Debito, 5m, new DateOnly(2026, 11, 2), null), default);

        var lista = await svc.ListarAsync(new DateOnly(2026, 11, 1), default);

        Assert.Single(lista);
        Assert.Equal(10m, lista[0].Valor);
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

