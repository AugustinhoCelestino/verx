using Verx.Contracts;

namespace Verx.Lancamentos.Api;

public sealed class Lancamento
{
    public Guid Id { get; private set; }
    public TipoLancamento Tipo { get; private set; }
    public decimal Valor { get; private set; }
    public DateOnly Data { get; private set; }
    public string? Descricao { get; private set; }
    public DateTimeOffset CriadoEm { get; private set; }
    public Guid UsuarioId { get; private set; }
    public string UsuarioNome { get; private set; } = string.Empty;

    private Lancamento() { }

    public static Lancamento Criar(TipoLancamento tipo, decimal valor, DateOnly data, string? descricao, UsuarioAtual usuario, DateTimeOffset agora)
    {
        if (!Enum.IsDefined(tipo)) throw new DomainException("Tipo de lançamento inválido.");
        if (valor <= 0) throw new DomainException("O valor deve ser maior que zero.");
        if (decimal.Round(valor, 2) != valor) throw new DomainException("O valor deve ter no máximo 2 casas decimais.");
        if (descricao?.Length > 200) throw new DomainException("A descrição deve ter no máximo 200 caracteres.");

        return new Lancamento
        {
            Id = Guid.NewGuid(),
            Tipo = tipo,
            Valor = valor,
            Data = data,
            Descricao = descricao,
            CriadoEm = agora,
            UsuarioId = usuario.Id,
            UsuarioNome = usuario.Nome
        };
    }
}

public sealed record UsuarioAtual(Guid Id, string Nome);

public sealed class Usuario
{
    public Guid Id { get; set; }
    public string Nome { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string SenhaHash { get; set; } = string.Empty;
    public DateTimeOffset CriadoEm { get; set; }
}

public sealed class CredenciaisInvalidasException() : Exception("E-mail ou senha inválidos.");

/// <summary>Transactional outbox: o evento é gravado na mesma transação do lançamento.</summary>
public sealed class OutboxMessage
{
    public Guid Id { get; set; }
    public string Payload { get; set; } = string.Empty;
    public DateTimeOffset CriadoEm { get; set; }
    public DateTimeOffset? PublicadoEm { get; set; }
    public int Tentativas { get; set; }
}

public sealed class DomainException(string message) : Exception(message);
