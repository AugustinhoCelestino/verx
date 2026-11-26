using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Verx.Contracts;
using Verx.Lancamentos.Api.Application;
using Verx.Lancamentos.Api.Domain;

namespace Verx.Lancamentos.Api.Infrastructure.Persistence;

public sealed class LancamentoRepository(LancamentosDbContext db) : ILancamentoRepository
{
    public void Adicionar(Lancamento lancamento) => db.Lancamentos.Add(lancamento);

    public async Task<IReadOnlyList<Lancamento>> ListarPorDataAsync(DateOnly data, CancellationToken ct)
    {
        var itens = await db.Lancamentos.AsNoTracking().Where(l => l.Data == data).ToListAsync(ct);
        return itens.OrderBy(l => l.CriadoEm).ToList();
    }
}

public sealed class UsuarioRepository(LancamentosDbContext db) : IUsuarioRepository
{
    public void Adicionar(Usuario usuario) => db.Usuarios.Add(usuario);

    public Task<Usuario?> ObterAsync(Guid id, CancellationToken ct) =>
        db.Usuarios.AsNoTracking().FirstOrDefaultAsync(u => u.Id == id, ct);

    public Task<Usuario?> ObterPorEmailAsync(Email email, CancellationToken ct) =>
        db.Usuarios.AsNoTracking().FirstOrDefaultAsync(u => u.Email == email, ct);

    public Task<bool> ExisteAsync(Email email, CancellationToken ct) =>
        db.Usuarios.AnyAsync(u => u.Email == email, ct);
}

public sealed class OutboxWriter(LancamentosDbContext db) : IOutbox
{
    public void Adicionar(LancamentoRegistrado evento, DateTimeOffset agora) =>
        db.Outbox.Add(new OutboxMessage { Id = evento.EventId, Payload = JsonSerializer.Serialize(evento), CriadoEm = agora });
}

public sealed class EfUnitOfWork(LancamentosDbContext db) : IUnitOfWork
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
}
