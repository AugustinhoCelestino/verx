namespace Verx.Lancamentos.Api.Domain;

public enum TipoLancamento
{
    Credito = 1,
    Debito = 2
}

/// <summary>Value object: quantia monetária positiva com no máximo 2 casas decimais.</summary>
public readonly struct Dinheiro : IEquatable<Dinheiro>
{
    public decimal Valor { get; }

    private Dinheiro(decimal valor) => Valor = valor;

    public static Dinheiro De(decimal valor)
    {
        if (valor <= 0) throw new DomainException("O valor deve ser maior que zero.");
        if (decimal.Round(valor, 2) != valor) throw new DomainException("O valor deve ter no máximo 2 casas decimais.");
        return new Dinheiro(valor);
    }

    public bool Equals(Dinheiro other) => Valor == other.Valor;
    public static bool operator ==(Dinheiro a, Dinheiro b) => a.Equals(b);
    public static bool operator !=(Dinheiro a, Dinheiro b) => !a.Equals(b);
    public override bool Equals(object? obj) => obj is Dinheiro d && Equals(d);
    public override int GetHashCode() => Valor.GetHashCode();
    public override string ToString() => Valor.ToString("F2");
}
