using Verx.Consolidado.Api.Domain;
using Verx.Contracts;

namespace Verx.Consolidado.Api.Application;

/// <summary>Casos de uso do consolidado: aplicar eventos de lançamento e consultar saldos.</summary>
public sealed class ConsolidadoService(
    ISaldoDiarioRepository saldos,
    IEventoProcessadoRepository eventos,
    IUnitOfWork uow,
    ISaldoCache cache,
    TimeProvider clock,
    ILogger<ConsolidadoService>? logger = null)
{
    private const int MaxTentativas = 5;

    /// <summary>Aplica o evento ao saldo do dia. Idempotente e seguro sob concorrência (otimista).</summary>
    public async Task<bool> ProcessarAsync(LancamentoRegistrado evento, CancellationToken ct)
    {
        // Retoma o trace da requisição de origem para que os logs dos dois serviços compartilhem o mesmo id.
        using var _ = Correlacao.Retomar(evento.CorrelationId, "processar-lancamento-registrado");

        for (var tentativa = 1; ; tentativa++)
        {
            if (await eventos.ExisteAsync(evento.EventId, ct))
            {
                logger?.LogInformation("Evento {EventId} já processado; ignorado.", evento.EventId);
                return false;
            }

            var saldo = await saldos.ObterOuCriarAsync(evento.Data, ct);
            var agora = clock.GetUtcNow();
            saldo.Aplicar((TipoMovimento)(int)evento.Tipo, evento.Valor, agora);
            eventos.Adicionar(new EventoProcessado(evento.EventId, agora));

            try
            {
                await uow.SalvarAsync(ct);
                cache.Invalidar(evento.Data);
                logger?.LogInformation("Evento {EventId} aplicado ao saldo de {Data}.", evento.EventId, evento.Data);
                return true;
            }
            catch (ConflitoDePersistenciaException) when (tentativa < MaxTentativas)
            {
                // Conflito de versão ou evento duplicado em paralelo: recarrega e reavalia.
                uow.Descartar();
            }
        }
    }

    public Task<SaldoDiarioResponse> ObterAsync(DateOnly data, CancellationToken ct) =>
        cache.ObterOuCriarAsync(data, async () =>
            await saldos.ConsultarAsync(data, ct) is { } saldo ? SaldoDiarioResponse.From(saldo) : SaldoDiarioResponse.Vazio(data));

    public async Task<IReadOnlyList<SaldoDiarioResponse>> ListarPeriodoAsync(DateOnly de, DateOnly ate, CancellationToken ct) =>
        (await saldos.ListarPeriodoAsync(de, ate, ct)).Select(SaldoDiarioResponse.From).ToList();
}
