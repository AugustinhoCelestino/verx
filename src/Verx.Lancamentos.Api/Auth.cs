using System.Net.Mail;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;

namespace Verx.Lancamentos.Api;

public sealed record RegistrarUsuarioRequest(string? Nome, string? Email, string? Senha);

public sealed record LoginRequest(string? Email, string? Senha);

public sealed record UsuarioResponse(Guid Id, string Nome, string Email);

public sealed record LoginResponse(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("token_type")] string TokenType,
    [property: JsonPropertyName("expires_in")] int ExpiresIn,
    UsuarioResponse Usuario);

public static class PasswordHasher
{
    private const int Iterations = 210_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    // Formato: iteracoes.salt.hash (base64), permitindo aumentar o custo no futuro.
    public static string Hash(string senha)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(senha, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string senha, string armazenado)
    {
        var partes = armazenado.Split('.');
        if (partes.Length != 3 || !int.TryParse(partes[0], out var iteracoes)) return false;
        var esperado = Convert.FromBase64String(partes[2]);
        var atual = Rfc2898DeriveBytes.Pbkdf2(senha, Convert.FromBase64String(partes[1]), iteracoes, HashAlgorithmName.SHA256, esperado.Length);
        return CryptographicOperations.FixedTimeEquals(atual, esperado);
    }
}

public sealed class AuthService(LancamentosDbContext db, JwtSettings jwt, TimeProvider clock)
{
    // Hash descartável: iguala o tempo de resposta quando o e-mail não existe.
    private static readonly string HashFalso = PasswordHasher.Hash("senha-inexistente");

    public async Task<LoginResponse> RegistrarAsync(RegistrarUsuarioRequest req, CancellationToken ct)
    {
        var nome = req.Nome?.Trim() ?? "";
        var email = NormalizarEmail(req.Email);
        if (nome.Length is 0 or > 100) throw new DomainException("Informe o nome (até 100 caracteres).");
        if (!EmailValido(email)) throw new DomainException("E-mail inválido.");
        if (req.Senha is null || req.Senha.Length < 8) throw new DomainException("A senha deve ter pelo menos 8 caracteres.");
        if (req.Senha.Length > 128) throw new DomainException("A senha deve ter no máximo 128 caracteres.");
        if (await db.Usuarios.AnyAsync(u => u.Email == email, ct)) throw new DomainException("E-mail já cadastrado.");

        var usuario = new Usuario
        {
            Id = Guid.NewGuid(),
            Nome = nome,
            Email = email,
            SenhaHash = PasswordHasher.Hash(req.Senha),
            CriadoEm = clock.GetUtcNow()
        };
        db.Usuarios.Add(usuario);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            throw new DomainException("E-mail já cadastrado.");
        }
        return CriarResposta(usuario);
    }

    public async Task<LoginResponse> LoginAsync(LoginRequest req, CancellationToken ct)
    {
        var email = NormalizarEmail(req.Email);
        var usuario = await db.Usuarios.AsNoTracking().FirstOrDefaultAsync(u => u.Email == email, ct);
        var ok = PasswordHasher.Verify(req.Senha ?? "", usuario?.SenhaHash ?? HashFalso);
        if (usuario is null || !ok) throw new CredenciaisInvalidasException();
        return CriarResposta(usuario);
    }

    public async Task<UsuarioResponse?> ObterAsync(Guid id, CancellationToken ct) =>
        await db.Usuarios.AsNoTracking().Where(u => u.Id == id)
            .Select(u => new UsuarioResponse(u.Id, u.Nome, u.Email)).FirstOrDefaultAsync(ct);

    private LoginResponse CriarResposta(Usuario u)
    {
        var (token, expira) = jwt.CreateToken(u);
        return new LoginResponse(token, "Bearer", expira, new UsuarioResponse(u.Id, u.Nome, u.Email));
    }

    private static string NormalizarEmail(string? email) => (email ?? "").Trim().ToLowerInvariant();

    private static bool EmailValido(string email) =>
        email.Length <= 200 && MailAddress.TryCreate(email, out var m) && m.Address == email;
}

public static class UsuarioClaims
{
    public static UsuarioAtual? Atual(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirst("sub")?.Value, out var id)
            ? new UsuarioAtual(id, user.FindFirst("name")?.Value ?? "")
            : null;
}
