using System.Net.Mail;

namespace Verx.Lancamentos.Api.Domain;

/// <summary>Value object: e-mail normalizado (minúsculas, sem espaços) e válido.</summary>
public readonly struct Email : IEquatable<Email>
{
    public const int TamanhoMaximo = 200;

    public string Valor { get; }

    private Email(string valor) => Valor = valor;

    public static Email Criar(string? valor)
    {
        var normalizado = (valor ?? "").Trim().ToLowerInvariant();
        if (!EhValido(normalizado)) throw new DomainException("E-mail inválido.");
        return new Email(normalizado);
    }

    /// <summary>Usado no login, onde um e-mail malformado é apenas "credenciais inválidas".</summary>
    public static bool TentarCriar(string? valor, out Email email)
    {
        var normalizado = (valor ?? "").Trim().ToLowerInvariant();
        var ok = EhValido(normalizado);
        email = ok ? new Email(normalizado) : default;
        return ok;
    }

    private static bool EhValido(string v) =>
        v.Length is > 0 and <= TamanhoMaximo && MailAddress.TryCreate(v, out var m) && m.Address == v;

    public bool Equals(Email other) => Valor == other.Valor;
    public static bool operator ==(Email a, Email b) => a.Equals(b);
    public static bool operator !=(Email a, Email b) => !a.Equals(b);
    public override bool Equals(object? obj) => obj is Email e && Equals(e);
    public override int GetHashCode() => Valor.GetHashCode();
    public override string ToString() => Valor;
}

public static class PoliticaDeSenha
{
    public const int Minimo = 8;
    public const int Maximo = 128;

    public static void Validar(string? senha)
    {
        if (senha is null || senha.Length < Minimo) throw new DomainException($"A senha deve ter pelo menos {Minimo} caracteres.");
        if (senha.Length > Maximo) throw new DomainException($"A senha deve ter no máximo {Maximo} caracteres.");
    }
}

/// <summary>Agregado raiz de identidade: quem registra os lançamentos.</summary>
public sealed class Usuario : AggregateRoot
{
    public const int TamanhoMaximoNome = 100;

    public Guid Id { get; private set; }
    public string Nome { get; private set; } = string.Empty;
    public Email Email { get; private set; }
    public string SenhaHash { get; private set; } = string.Empty;
    public DateTimeOffset CriadoEm { get; private set; }

    private Usuario() { }

    public static Usuario Criar(string? nome, Email email, string senhaHash, DateTimeOffset agora)
    {
        var nomeLimpo = nome?.Trim() ?? "";
        if (nomeLimpo.Length is 0 or > TamanhoMaximoNome)
            throw new DomainException($"Informe o nome (até {TamanhoMaximoNome} caracteres).");

        return new Usuario { Id = Guid.NewGuid(), Nome = nomeLimpo, Email = email, SenhaHash = senhaHash, CriadoEm = agora };
    }
}

public sealed class CredenciaisInvalidasException() : Exception("E-mail ou senha inválidos.");
