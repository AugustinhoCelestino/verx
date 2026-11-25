using Verx.Contracts;

namespace Verx.Consolidado.Api;

public sealed class SaldoDiario
{
    public DateOnly Data { get; private set; }
    public decimal TotalCreditos { get; private set; }
    public decimal TotalDebitos { get; private set; }
    public int QuantidadeLancamentos { get; private set; }
    public DateTimeOffset AtualizadoEm { get; private set; }
    /// <summary>Token de concorrência otimista, portátil entre providers.</summary>
    public int Versao { get; private set; }

    public decimal Saldo => TotalCreditos - TotalDebitos;

    private SaldoDiario() { }

    public SaldoDiario(DateOnly data) => Data = data;

    public void Aplicar(TipoLancamento tipo, decimal valor, DateTimeOffset agora)
    {
        if (tipo == TipoLancamento.Credito) TotalCreditos += valor; else TotalDebitos += valor;
        QuantidadeLancamentos++;
        AtualizadoEm = agora;
        Versao++;
    }
}

/// <summary>Registro de eventos já aplicados, garante idempotência (entrega at-least-once).</summary>
public sealed class EventoProcessado
{
    public Guid EventId { get; set; }
    public DateTimeOffset ProcessadoEm { get; set; }
}

public sealed record SaldoDiarioResponse(DateOnly Data, decimal TotalCreditos, decimal TotalDebitos, decimal Saldo, int QuantidadeLancamentos, DateTimeOffset? AtualizadoEm)
{
    public static SaldoDiarioResponse Vazio(DateOnly data) => new(data, 0, 0, 0, 0, null);

    public static SaldoDiarioResponse From(SaldoDiario s) =>
        new(s.Data, s.TotalCreditos, s.TotalDebitos, s.Saldo, s.QuantidadeLancamentos, s.AtualizadoEm);
}
