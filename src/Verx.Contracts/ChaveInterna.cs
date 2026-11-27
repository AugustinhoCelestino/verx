using System.Security.Cryptography;
using System.Text;

namespace Verx.Contracts;

/// <summary>Chave compartilhada das rotas internas do modo local (HTTP). Sem chave configurada, as rotas ficam desabilitadas.</summary>
public static class ChaveInterna
{
    public const string Cabecalho = "X-Api-Key";
    public const int TamanhoMinimo = 16;

    public static bool Confere(string? recebida, string? esperada) =>
        !string.IsNullOrEmpty(esperada)
        && CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(recebida ?? ""), Encoding.UTF8.GetBytes(esperada));

    /// <summary>Em qualquer ambiente, recusa chave ausente ou curta: não existe chave padrão no repositório.</summary>
    public static void ExigirSegura(string? chave, string nome)
    {
        if (string.IsNullOrWhiteSpace(chave) || chave.Length < TamanhoMinimo)
            throw new InvalidOperationException($"{nome} não configurada com um segredo forte; defina-a por variável de ambiente ou cofre de segredos.");
    }
}
