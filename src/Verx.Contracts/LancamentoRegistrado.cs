namespace Verx.Contracts;

public enum TipoLancamento
{
    Credito = 1,
    Debito = 2
}

/// <summary>Evento de integração publicado pelo serviço de Lançamentos.</summary>
/// <param name="CorrelationId">Id de rastreio (trace id W3C) da requisição que originou o evento; opcional.</param>
public sealed record LancamentoRegistrado(
    Guid EventId,
    Guid LancamentoId,
    TipoLancamento Tipo,
    decimal Valor,
    DateOnly Data,
    DateTimeOffset OcorridoEm,
    string? CorrelationId = null);
