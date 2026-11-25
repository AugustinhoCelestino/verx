using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Verx.Contracts;

namespace Verx.Lancamentos.Api;

public sealed record RegistrarLancamentoRequest(TipoLancamento Tipo, decimal Valor, DateOnly? Data, string? Descricao);

public sealed record LancamentoResponse(Guid Id, TipoLancamento Tipo, decimal Valor, DateOnly Data, string? Descricao, DateTimeOffset CriadoEm, Guid UsuarioId, string UsuarioNome)
{
    public static LancamentoResponse From(Lancamento l) =>
        new(l.Id, l.Tipo, l.Valor, l.Data, l.Descricao, l.CriadoEm, l.UsuarioId, l.UsuarioNome);
}

public sealed class LancamentoService(LancamentosDbContext db, TimeProvider clock)
{
    public async Task<LancamentoResponse>     RegistrarAsync(RegistrarLancamentoRequest req, UsuarioAtual usuario, CancellationToken ct)
    {
        var agora = clock.GetUtcNow();
        var data = req.Data ?? DateOnly.FromDateTime(agora.UtcDateTime);
            var lancamento = Lancamento.Criar(req.Tipo, req.Valor, data, req.Descricao, usuario, agora);

        var evento = new LancamentoRegistrado(Guid.NewGuid(), lancamento.Id, lancamento.Tipo, lancamento.Valor, lancamento.Data, agora);

        db.Lancamentos.Add(lancamento);
        db.Outbox.Add(new OutboxMessage
        {
            Id = evento.EventId,
            Payload = JsonSerializer.Serialize(evento),
            CriadoEm = agora
        });

        // Uma única transação: lançamento e evento são persistidos juntos ou nenhum dos dois.
        await db.SaveChangesAsync(ct);
        return LancamentoResponse.From(lancamento);
    }

    public async Task<IReadOnlyList<LancamentoResponse>> ListarAsync(DateOnly data, CancellationToken ct)
    {
        var itens = await db.Lancamentos.AsNoTracking()
            .Where(l => l.Data == data)
            .ToListAsync(ct);
        return itens.OrderBy(l => l.CriadoEm).Select(LancamentoResponse.From).ToList();
    }
}
