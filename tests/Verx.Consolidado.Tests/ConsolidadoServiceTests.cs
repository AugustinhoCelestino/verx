using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Verx.Consolidado.Api;
using Verx.Contracts;

namespace Verx.Consolidado.Tests;

public sealed class ConsolidadoServiceTests : IAsyncLifetime
{
    private readonly SqliteConnection _conn = new("DataSource=:memory:");
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private ConsolidadoDbContext _db = null!;
    private static readonly DateOnly Dia = new(2026, 11, 24);

    public async Task InitializeAsync()
    {
        await _conn.OpenAsync();
        _db = NewContext();
        await _db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        _cache.Dispose();
        await _conn.DisposeAsync();
    }

    private ConsolidadoDbContext NewContext() =>
        new(new DbContextOptionsBuilder<ConsolidadoDbContext>().UseSqlite(_conn).Options);

    private ConsolidadoService Service(ConsolidadoDbContext? db = null) =>
        new(db ?? _db, _cache, TimeProvider.System, new ConsolidadoOptions { TtlSeconds = 60 });

    private static LancamentoRegistrado Evento(TipoLancamento tipo, decimal valor, Guid? id = null) =>
        new(id ?? Guid.NewGuid(), Guid.NewGuid(), tipo, valor, Dia, DateTimeOffset.UtcNow);

    [Fact]
    public async Task Saldo_e_creditos_menos_debitos()
    {
        var svc = Service();
        await svc.ProcessarAsync(Evento(TipoLancamento.Credito, 100m), default);
        await svc.ProcessarAsync(Evento(TipoLancamento.Credito, 50.25m), default);
        await svc.ProcessarAsync(Evento(TipoLancamento.Debito, 30m), default);

        var r = await svc.ObterAsync(Dia, default);

        Assert.Equal(150.25m, r.TotalCreditos);
        Assert.Equal(30m, r.TotalDebitos);
        Assert.Equal(120.25m, r.Saldo);
        Assert.Equal(3, r.QuantidadeLancamentos);
    }

    [Fact]
    public async Task Evento_duplicado_nao_e_aplicado_duas_vezes()
    {
        var svc = Service();
        var evento = Evento(TipoLancamento.Credito, 100m);

        Assert.True(await svc.ProcessarAsync(evento, default));
        Assert.False(await svc.ProcessarAsync(evento, default));

        Assert.Equal(100m, (await svc.ObterAsync(Dia, default)).Saldo);
    }

    [Fact]
    public async Task Dia_sem_movimento_retorna_saldo_zero()
    {
        var r = await Service().ObterAsync(new DateOnly(2020, 1, 1), default);

        Assert.Equal(0m, r.Saldo);
        Assert.Null(r.AtualizadoEm);
    }

    [Fact]
    public async Task Processar_invalida_o_cache_do_dia()
    {
        var svc = Service();
        await svc.ProcessarAsync(Evento(TipoLancamento.Credito, 10m), default);
        Assert.Equal(10m, (await svc.ObterAsync(Dia, default)).Saldo);

        await svc.ProcessarAsync(Evento(TipoLancamento.Credito, 5m), default);

        Assert.Equal(15m, (await svc.ObterAsync(Dia, default)).Saldo);
    }

    [Fact]
    public async Task Processamento_concorrente_nao_perde_atualizacoes()
    {
        // Dois contextos independentes (como duas instÃ¢ncias do consumidor) sobre o mesmo saldo.
        await using var outro = NewContext();
        await Service().ProcessarAsync(Evento(TipoLancamento.Credito, 1m), default);

        var a = Service(_db);
        var b = Service(outro);
        // Carrega o saldo nos dois contextos antes de gravar para forÃ§ar conflito de versÃ£o.
        await _db.Saldos.ToListAsync();
        await outro.Saldos.ToListAsync();
        await a.ProcessarAsync(Evento(TipoLancamento.Credito, 10m), default);
        await b.ProcessarAsync(Evento(TipoLancamento.Credito, 100m), default);

        var r = await Service(NewContext()).ListarPeriodoAsync(Dia, Dia, default);
        Assert.Equal(111m, r[0].Saldo);
    }
}

