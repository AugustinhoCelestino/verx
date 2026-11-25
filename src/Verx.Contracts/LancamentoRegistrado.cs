namespace Verx.Contracts;

public enum TipoLancamento
{
    Credito = 1,
    Debito = 2
}

/// <summary>Evento de integração publicado pelo serviço de Lançamentos.</summary>
public sealed record LancamentoRegistrado(
    Guid EventId,
    Guid LancamentoId,
    TipoLancamento Tipo,
    decimal Valor,
    DateOnly Data,
    DateTimeOffset OcorridoEm);
