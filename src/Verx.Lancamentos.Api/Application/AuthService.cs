using Verx.Lancamentos.Api.Domain;

namespace Verx.Lancamentos.Api.Application;

/// <summary>Caso de uso: cadastro e autenticação de usuários.</summary>
public sealed class AuthService(IUsuarioRepository usuarios, ISenhaHasher hasher, ITokenService tokens, IUnitOfWork uow, TimeProvider clock)
{
    public async Task<LoginResponse> RegistrarAsync(RegistrarUsuarioRequest req, CancellationToken ct)
    {
        var email = Email.Criar(req.Email);
        PoliticaDeSenha.Validar(req.Senha);
        if (await usuarios.ExisteAsync(email, ct)) throw new DomainException("E-mail já cadastrado.");

        var usuario = Usuario.Criar(req.Nome, email, hasher.Gerar(req.Senha!), clock.GetUtcNow());
        usuarios.Adicionar(usuario);
        try
        {
            await uow.SalvarAsync(ct);
        }
        catch (ConflitoDePersistenciaException)
        {
            throw new DomainException("E-mail já cadastrado.");
        }
        return CriarResposta(usuario);
    }

    public async Task<LoginResponse> LoginAsync(LoginRequest req, CancellationToken ct)
    {
        var existe = Email.TentarCriar(req.Email, out var email);
        var usuario = existe ? await usuarios.ObterPorEmailAsync(email, ct) : null;
        var ok = hasher.Verificar(req.Senha ?? "", usuario?.SenhaHash);
        if (usuario is null || !ok) throw new CredenciaisInvalidasException();
        return CriarResposta(usuario);
    }

    public async Task<UsuarioResponse?> ObterAsync(Guid id, CancellationToken ct) =>
        await usuarios.ObterAsync(id, ct) is { } u ? new UsuarioResponse(u.Id, u.Nome, u.Email.Valor) : null;

    private LoginResponse CriarResposta(Usuario u)
    {
        var (token, expira) = tokens.Gerar(u);
        return new LoginResponse(token, "Bearer", expira, new UsuarioResponse(u.Id, u.Nome, u.Email.Valor));
    }
}
