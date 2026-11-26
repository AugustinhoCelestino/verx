namespace Verx.Lancamentos.Api.Domain;

public sealed record LancamentoRegistradoEvent(
    Guid LancamentoId,
    TipoLancamento Tipo,
    Dinheiro Valor,
    DateOnly Data,
    Guid UsuarioId,
    DateTimeOffset OcorridoEm) : IDomainEvent;

/// <summary>Agregado raiz do contexto de Lançamentos. Um lançamento é imutável depois de registrado.</summary>
public sealed class Lancamento : AggregateRoot
{
    public const int TamanhoMaximoDescricao = 200;

    public Guid Id { get; private set; }
    public TipoLancamento Tipo { get; private set; }
    public Dinheiro Valor { get; private set; }
    public DateOnly Data { get; private set; }
    public string? Descricao { get; private set; }
    public DateTimeOffset CriadoEm { get; private set; }
    public Guid UsuarioId { get; private set; }
    public string UsuarioNome { get; private set; } = string.Empty;

    private Lancamento() { }

    public static Lancamento Registrar(
        TipoLancamento tipo, decimal valor, DateOnly data, string? descricao,
        Guid usuarioId, string usuarioNome, DateTimeOffset agora)
    {
        if (!Enum.IsDefined(tipo)) throw new DomainException("Tipo de lançamento inválido.");
        if (descricao?.Length > TamanhoMaximoDescricao)
            throw new DomainException($"A descrição deve ter no máximo {TamanhoMaximoDescricao} caracteres.");
        if (usuarioId == Guid.Empty) throw new DomainException("O lançamento deve ter um usuário responsável.");

        var lancamento = new Lancamento
        {
            Id = Guid.NewGuid(),
            Tipo = tipo,
            Valor = Dinheiro.De(valor),
            Data = data,
            Descricao = descricao,
            CriadoEm = agora,
            UsuarioId = usuarioId,
            UsuarioNome = usuarioNome
        };
        lancamento.AdicionarEvento(new LancamentoRegistradoEvent(lancamento.Id, tipo, lancamento.Valor, data, usuarioId, agora));
        return lancamento;
    }
}
