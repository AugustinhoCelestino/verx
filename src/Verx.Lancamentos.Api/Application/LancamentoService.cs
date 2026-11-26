using Verx.Contracts;
using Verx.Lancamentos.Api.Domain;
using TipoContrato = Verx.Contracts.TipoLancamento;

namespace Verx.Lancamentos.Api.Application;

/// <summary>Caso de uso: registrar e consultar lançamentos. Orquestra; as regras vivem no domínio.</summary>
public sealed class LancamentoService(ILancamentoRepository lancamentos, IOutbox outbox, IUnitOfWork uow, TimeProvider clock)
{
    public async Task<LancamentoResponse> RegistrarAsync(RegistrarLancamentoRequest req, UsuarioAtual usuario, CancellationToken ct)
    {
        var agora = clock.GetUtcNow();
        var data = req.Data ?? DateOnly.FromDateTime(agora.UtcDateTime);
        var lancamento = Lancamento.Registrar(req.Tipo, req.Valor, data, req.Descricao, usuario.Id, usuario.Nome, agora);

        lancamentos.Adicionar(lancamento);
        // Eventos de domínio viram eventos de integração (contrato público) no outbox.
        foreach (var e in lancamento.EventosDeDominio.OfType<LancamentoRegistradoEvent>())
            outbox.Adicionar(new LancamentoRegistrado(Guid.NewGuid(), e.LancamentoId, (TipoContrato)(int)e.Tipo, e.Valor.Valor, e.Data, e.OcorridoEm), agora);
        lancamento.LimparEventos();

        // Uma única transação: lançamento e evento são persistidos juntos ou nenhum dos dois.
        await uow.SalvarAsync(ct);
        return LancamentoResponse.From(lancamento);
    }

    public async Task<IReadOnlyList<LancamentoResponse>> ListarAsync(DateOnly data, CancellationToken ct) =>
        (await lancamentos.ListarPorDataAsync(data, ct)).Select(LancamentoResponse.From).ToList();
}
