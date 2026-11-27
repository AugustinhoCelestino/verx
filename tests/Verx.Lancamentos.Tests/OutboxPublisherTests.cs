using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Verx.Lancamentos.Api.Infrastructure.Messaging;
using Verx.Lancamentos.Api.Infrastructure.Persistence;

namespace Verx.Lancamentos.Tests;

public sealed class OutboxPublisherTests : IAsyncLifetime
{
    private readonly SqliteConnection _conn = new("DataSource=:memory:");
    private ServiceProvider _sp = null!;
    private readonly DateTimeOffset _now = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);

    public async Task InitializeAsync()
    {
        await _conn.OpenAsync();
        _sp = new ServiceCollection()
            .AddDbContext<LancamentosDbContext>(o => o.UseSqlite(_conn))
            .BuildServiceProvider();
        using var scope = _sp.CreateScope();
        await scope.ServiceProvider.GetRequiredService<LancamentosDbContext>().Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _sp.DisposeAsync();
        await _conn.DisposeAsync();
    }

    private OutboxPublisher Publisher() =>
        new(_sp.GetRequiredService<IServiceScopeFactory>(), new SemTransporte(), new FixedClock(_now), NullLogger<OutboxPublisher>.Instance);

    private async Task<(Guid venenosa, Guid boa)> SemearAsync()
    {
        using var scope = _sp.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LancamentosDbContext>();
        var venenosa = new OutboxMessage { Id = Guid.NewGuid(), Payload = "{}", CriadoEm = _now };
        var boa = new OutboxMessage { Id = Guid.NewGuid(), Payload = "{}", CriadoEm = _now.AddSeconds(1) };
        db.Outbox.AddRange(venenosa, boa);
        await db.SaveChangesAsync();
        return (venenosa.Id, boa.Id);
    }

    private async Task<OutboxMessage> LerAsync(Guid id)
    {
        using var scope = _sp.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<LancamentosDbContext>().Outbox.AsNoTracking().SingleAsync(m => m.Id == id);
    }

    [Fact]
    public async Task Mensagem_recusada_nao_bloqueia_as_seguintes()
    {
        var (venenosa, boa) = await SemearAsync();
        var transporte = new TransporteFake(recusar: venenosa);

        var publicadas = await Publisher().PublicarPendentesAsync(transporte, default);

        Assert.Equal(1, publicadas);
        Assert.NotNull((await LerAsync(boa)).PublicadoEm);
        var v = await LerAsync(venenosa);
        Assert.Null(v.PublicadoEm);
        Assert.Equal(1, v.Tentativas);
        Assert.Null(v.DescartadoEm);
    }

    [Fact]
    public async Task Mensagem_e_descartada_apos_5_tentativas_e_nao_e_mais_enviada()
    {
        var (venenosa, _) = await SemearAsync();
        var transporte = new TransporteFake(recusar: venenosa);
        var publisher = Publisher();

        for (var i = 0; i < 5; i++) await publisher.PublicarPendentesAsync(transporte, default);

        var v = await LerAsync(venenosa);
        Assert.Equal(5, v.Tentativas);
        Assert.Equal(_now, v.DescartadoEm);
        Assert.NotNull(v.UltimoErro);

        var enviosAntes = transporte.Enviadas.Count(id => id == venenosa);
        await publisher.PublicarPendentesAsync(transporte, default);
        Assert.Equal(enviosAntes, transporte.Enviadas.Count(id => id == venenosa));
    }

    [Fact]
    public async Task Indisponibilidade_do_destino_nao_consome_tentativas()
    {
        var (venenosa, _) = await SemearAsync();
        var transporte = new TransporteFake(indisponivel: true);

        await Assert.ThrowsAsync<HttpRequestException>(() => Publisher().PublicarPendentesAsync(transporte, default));

        Assert.Equal(0, (await LerAsync(venenosa)).Tentativas);
    }

    private sealed class TransporteFake(Guid? recusar = null, bool indisponivel = false) : IOutboxTransport
    {
        public List<Guid> Enviadas { get; } = [];
        public bool IsOpen => true;

        public Task SendAsync(OutboxMessage message, CancellationToken ct)
        {
            Enviadas.Add(message.Id);
            if (indisponivel) throw new HttpRequestException("fora do ar");
            if (message.Id == recusar) throw new MensagemRecusadaException("500");
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    private sealed class SemTransporte : IOutboxTransportFactory
    {
        public Task<IOutboxTransport> ConnectAsync(CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
