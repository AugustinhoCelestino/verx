namespace Verx.Lancamentos.Api.Domain;

public interface ILancamentoRepository
{
    void Adicionar(Lancamento lancamento);
    Task<IReadOnlyList<Lancamento>> ListarPorDataAsync(DateOnly data, CancellationToken ct);
}

public interface IUsuarioRepository
{
    void Adicionar(Usuario usuario);
    Task<Usuario?> ObterAsync(Guid id, CancellationToken ct);
    Task<Usuario?> ObterPorEmailAsync(Email email, CancellationToken ct);
    Task<bool> ExisteAsync(Email email, CancellationToken ct);
}

/// <summary>Serviço de domínio: política de armazenamento e verificação de senha.</summary>
public interface ISenhaHasher
{
    string Gerar(string senha);

    /// <summary>Com <paramref name="hash"/> nulo (usuário inexistente) ainda gasta o mesmo tempo, evitando revelar quais e-mails existem.</summary>
    bool Verificar(string senha, string? hash);
}
