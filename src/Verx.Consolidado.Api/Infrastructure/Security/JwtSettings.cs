using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace Verx.Consolidado.Api.Infrastructure.Security;

public sealed class JwtSettings
{
    public string Issuer { get; set; } = "verx";
    public string Audience { get; set; } = "verx-api";
    public string Key { get; set; } = "";

    /// <summary>Não há chave padrão: sem segredo configurado (variável de ambiente, user-secrets ou cofre) o serviço não sobe.</summary>
    public void ExigirChaveSegura()
    {
        if (Encoding.UTF8.GetByteCount(Key ?? "") < 32)
            throw new InvalidOperationException("Jwt:Key não configurada com um segredo forte (mín. 32 bytes); defina-a por variável de ambiente, user-secrets ou cofre de segredos.");
    }

    public TokenValidationParameters ValidationParameters() => new()
    {
        ValidIssuer = Issuer,
        ValidAudience = Audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)),
        ValidateIssuerSigningKey = true,
        NameClaimType = "name",
        RoleClaimType = "role"
    };
}
