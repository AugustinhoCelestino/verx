namespace Verx.Consolidado.Api.Domain;

public sealed class DomainException(string message) : Exception(message);

public enum TipoMovimento
{
    Credito = 1,
    Debito = 2
}

/// <summary>Agregado raiz: o saldo consolidado de um dia. Só é alterado por <see cref="Aplicar"/>.</summary>
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

    public void Aplicar(TipoMovimento tipo, decimal valor, DateTimeOffset agora)
    {
        if (!Enum.IsDefined(tipo)) throw new DomainException("Tipo de movimento inválido.");
        if (valor <= 0) throw new DomainException("O valor do movimento deve ser maior que zero.");

        if (tipo == TipoMovimento.Credito) TotalCreditos += valor; else TotalDebitos += valor;
        QuantidadeLancamentos++;
        AtualizadoEm = agora;
        Versao++;
    }
}

/// <summary>Registro de eventos já aplicados, garante idempotência (entrega at-least-once).</summary>
public sealed class EventoProcessado
{
    public Guid EventId { get; private set; }
    public DateTimeOffset ProcessadoEm { get; private set; }

    private EventoProcessado() { }

    public EventoProcessado(Guid eventId, DateTimeOffset processadoEm)
    {
        EventId = eventId;
        ProcessadoEm = processadoEm;
    }
}

public interface ISaldoDiarioRepository
{
    /// <summary>Carrega o saldo para alteração, ou cria um novo (vazio) se o dia ainda não tem movimento.</summary>
    Task<SaldoDiario> ObterOuCriarAsync(DateOnly data, CancellationToken ct);
    Task<SaldoDiario?> ConsultarAsync(DateOnly data, CancellationToken ct);
    Task<IReadOnlyList<SaldoDiario>> ListarPeriodoAsync(DateOnly de, DateOnly ate, CancellationToken ct);
}

public interface IEventoProcessadoRepository
{
    Task<bool> ExisteAsync(Guid eventId, CancellationToken ct);
    void Adicionar(EventoProcessado evento);
}
