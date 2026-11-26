namespace Verx.Lancamentos.Api.Domain;

/// <summary>Violação de uma regra de negócio (invariante do domínio).</summary>
public sealed class DomainException(string message) : Exception(message);

public interface IDomainEvent
{
    DateTimeOffset OcorridoEm { get; }
}

/// <summary>Raiz de agregado: único ponto de entrada para alterar o agregado e registrar eventos de domínio.</summary>
public abstract class AggregateRoot
{
    private readonly List<IDomainEvent> _eventos = [];

    public IReadOnlyCollection<IDomainEvent> EventosDeDominio => _eventos;

    protected void AdicionarEvento(IDomainEvent evento) => _eventos.Add(evento);

    public void LimparEventos() => _eventos.Clear();
}
