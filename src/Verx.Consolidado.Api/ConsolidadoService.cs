using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Verx.Contracts;

namespace Verx.Consolidado.Api;

public sealed class ConsolidadoService(
    ConsolidadoDbContext db,
    IMemoryCache cache,
    TimeProvider clock,
    ConsolidadoOptions options)
{
    private const int MaxTentativas = 5;

    public static string CacheKey(DateOnly data) => $"saldo:{data:yyyy-MM-dd}";

    /// <summary>Aplica o evento ao saldo do dia. Idempotente e seguro sob concorrência (otimista).</summary>
    public async Task<bool> ProcessarAsync(LancamentoRegistrado evento, CancellationToken ct)
    {
        for (var tentativa = 1; ; tentativa++)
        {
            if (await db.EventosProcessados.AnyAsync(e => e.EventId == evento.EventId, ct))
                return false;

            var saldo = await db.Saldos.FindAsync([evento.Data], ct);
            if (saldo is null)
            {
                saldo = new SaldoDiario(evento.Data);
                db.Saldos.Add(saldo);
            }

            var agora = clock.GetUtcNow();
            saldo.Aplicar(evento.Tipo, evento.Valor, agora);
            db.EventosProcessados.Add(new EventoProcessado { EventId = evento.EventId, ProcessadoEm = agora });

            try
            {
                await db.SaveChangesAsync(ct);
                cache.Remove(CacheKey(evento.Data));
                return true;
            }
            catch (DbUpdateException) when (tentativa < MaxTentativas)
            {
                // Conflito de versão ou evento duplicado em paralelo: recarrega e reavalia.
                db.ChangeTracker.Clear();
            }
        }
    }

    public async Task<SaldoDiarioResponse> ObterAsync(DateOnly data, CancellationToken ct)
    {
        var resultado = await cache.GetOrCreateAsync(CacheKey(data), async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(options.TtlSeconds);
            var saldo = await db.Saldos.AsNoTracking().FirstOrDefaultAsync(s => s.Data == data, ct);
            return saldo is null ? SaldoDiarioResponse.Vazio(data) : SaldoDiarioResponse.From(saldo);
        });
        return resultado!;
    }

    public async Task<IReadOnlyList<SaldoDiarioResponse>> ListarPeriodoAsync(DateOnly de, DateOnly ate, CancellationToken ct)
    {
        var itens = await db.Saldos.AsNoTracking()
            .Where(s => s.Data >= de && s.Data <= ate)
            .OrderBy(s => s.Data)
            .ToListAsync(ct);
        return itens.Select(SaldoDiarioResponse.From).ToList();
    }
}

public sealed class ConsolidadoOptions
{
    public int TtlSeconds { get; set; } = 10;
}
