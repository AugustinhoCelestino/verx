using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Verx.Lancamentos.Api.Application;
using Verx.Lancamentos.Api.Domain;

namespace Verx.Lancamentos.Api.Infrastructure.Security;

public sealed class JwtSettings
{
    public string Issuer { get; set; } = "verx";
    public string Audience { get; set; } = "verx-api";
    public string Key { get; set; } = "dev-only-signing-key-change-me-32bytes!!";

    public int ExpiresMinutes { get; set; } = 480;

    private SymmetricSecurityKey SecurityKey => new(Encoding.UTF8.GetBytes(Key));

    public TokenValidationParameters ValidationParameters() => new()
    {
        ValidIssuer = Issuer,
        ValidAudience = Audience,
        IssuerSigningKey = SecurityKey,
        ValidateIssuerSigningKey = true,
        NameClaimType = "name"
    };

    public (string Token, int ExpiresInSeconds) CreateToken(Usuario usuario)
    {
        var expires = TimeSpan.FromMinutes(ExpiresMinutes);
        var token = new JwtSecurityToken(Issuer, Audience,
            [new Claim("sub", usuario.Id.ToString()), new Claim("name", usuario.Nome), new Claim("email", usuario.Email.Valor)],
            expires: DateTime.UtcNow.Add(expires),
            signingCredentials: new SigningCredentials(SecurityKey, SecurityAlgorithms.HmacSha256));
        return (new JwtSecurityTokenHandler().WriteToken(token), (int)expires.TotalSeconds);
    }
}

public sealed class JwtTokenService(JwtSettings settings) : ITokenService
{
    public (string Token, int ExpiresInSeconds) Gerar(Usuario usuario) => settings.CreateToken(usuario);
}

public sealed class Pbkdf2SenhaHasher : ISenhaHasher
{
    private const int Iterations = 210_000;
    private const int SaltSize = 16;
    private const int HashSize = 32;

    // Hash descartável: iguala o tempo de resposta quando o usuário não existe.
    private static readonly string HashFalso = new Pbkdf2SenhaHasher().Gerar("senha-inexistente");

    // Formato: iteracoes.salt.hash (base64), permitindo aumentar o custo no futuro.
    public string Gerar(string senha)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltSize);
        var hash = Rfc2898DeriveBytes.Pbkdf2(senha, salt, Iterations, HashAlgorithmName.SHA256, HashSize);
        return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    public bool Verificar(string senha, string? armazenado)
    {
        var partes = (armazenado ?? HashFalso).Split('.');
        if (partes.Length != 3 || !int.TryParse(partes[0], out var iteracoes)) return false;
        var esperado = Convert.FromBase64String(partes[2]);
        var atual = Rfc2898DeriveBytes.Pbkdf2(senha, Convert.FromBase64String(partes[1]), iteracoes, HashAlgorithmName.SHA256, esperado.Length);
        return CryptographicOperations.FixedTimeEquals(atual, esperado) && armazenado is not null;
    }
}
