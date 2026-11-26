using Verx.Consolidado.Api.Domain;

namespace Verx.Consolidado.Api.Application;

public sealed record SaldoDiarioResponse(DateOnly Data, decimal TotalCreditos, decimal TotalDebitos, decimal Saldo, int QuantidadeLancamentos, DateTimeOffset? AtualizadoEm)
{
    public static SaldoDiarioResponse Vazio(DateOnly data) => new(data, 0, 0, 0, 0, null);

    public static SaldoDiarioResponse From(SaldoDiario s) =>
        new(s.Data, s.TotalCreditos, s.TotalDebitos, s.Saldo, s.QuantidadeLancamentos, s.AtualizadoEm);
}

public sealed class ConsolidadoOptions
{
    public int TtlSeconds { get; set; } = 10;
}

public interface IUnitOfWork
{
    /// <exception cref="ConflitoDePersistenciaException">Conflito de versão ou evento duplicado em paralelo.</exception>
    Task SalvarAsync(CancellationToken ct);

    /// <summary>Descarta o estado carregado para reavaliar o caso de uso do zero.</summary>
    void Descartar();
}

public interface ISaldoCache
{
    Task<SaldoDiarioResponse> ObterOuCriarAsync(DateOnly data, Func<Task<SaldoDiarioResponse>> fabrica);
    void Invalidar(DateOnly data);
}

public sealed class ConflitoDePersistenciaException(Exception inner) : Exception("Conflito ao persistir.", inner);
