using Verx.Contracts;
using Verx.Lancamentos.Api.Domain;

namespace Verx.Lancamentos.Api.Application;

/// <summary>Confirma as alterações do caso de uso numa única transação.</summary>
public interface IUnitOfWork
{
    /// <exception cref="ConflitoDePersistenciaException">Violação de unicidade ou concorrência.</exception>
    Task SalvarAsync(CancellationToken ct);
}

/// <summary>Porta do transactional outbox: o evento entra na mesma transação do agregado.</summary>
public interface IOutbox
{
    void Adicionar(LancamentoRegistrado evento, DateTimeOffset agora);
}

public interface ITokenService
{
    (string Token, int ExpiresInSeconds) Gerar(Usuario usuario);
}

public sealed class ConflitoDePersistenciaException(Exception inner) : Exception("Conflito ao persistir.", inner);
