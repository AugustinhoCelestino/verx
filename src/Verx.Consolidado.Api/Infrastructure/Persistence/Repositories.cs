using Microsoft.EntityFrameworkCore;
using Verx.Consolidado.Api.Application;
using Verx.Consolidado.Api.Domain;

namespace Verx.Consolidado.Api.Infrastructure.Persistence;

public sealed class SaldoDiarioRepository(ConsolidadoDbContext db) : ISaldoDiarioRepository
{
    public async Task<SaldoDiario> ObterOuCriarAsync(DateOnly data, CancellationToken ct)
    {
        var saldo = await db.Saldos.FindAsync([data], ct);
        if (saldo is null)
        {
            saldo = new SaldoDiario(data);
            db.Saldos.Add(saldo);
        }
        return saldo;
    }

    public Task<SaldoDiario?> ConsultarAsync(DateOnly data, CancellationToken ct) =>
        db.Saldos.AsNoTracking().FirstOrDefaultAsync(s => s.Data == data, ct);

    public async Task<IReadOnlyList<SaldoDiario>> ListarPeriodoAsync(DateOnly de, DateOnly ate, CancellationToken ct) =>
        await db.Saldos.AsNoTracking().Where(s => s.Data >= de && s.Data <= ate).OrderBy(s => s.Data).ToListAsync(ct);
}

public sealed class EventoProcessadoRepository(ConsolidadoDbContext db) : IEventoProcessadoRepository
{
    public Task<bool> ExisteAsync(Guid eventId, CancellationToken ct) =>
        db.EventosProcessados.AnyAsync(e => e.EventId == eventId, ct);

    public void Adicionar(EventoProcessado evento) => db.EventosProcessados.Add(evento);
}

public sealed class EfUnitOfWork(ConsolidadoDbContext db) : IUnitOfWork
{
    public async Task SalvarAsync(CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex)
        {
            throw new ConflitoDePersistenciaException(ex);
        }
    }

    public void Descartar() => db.ChangeTracker.Clear();
}
