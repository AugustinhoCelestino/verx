using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace Verx.Lancamentos.Api;

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
            [new Claim("sub", usuario.Id.ToString()), new Claim("name", usuario.Nome), new Claim("email", usuario.Email)],
            expires: DateTime.UtcNow.Add(expires),
            signingCredentials: new SigningCredentials(SecurityKey, SecurityAlgorithms.HmacSha256));
        return (new JwtSecurityTokenHandler().WriteToken(token), (int)expires.TotalSeconds);
    }
}

public static class DatabaseInitializer
{
    // O SQL Server do compose pode demorar a ficar pronto; tenta por ~60s.
    public static async Task EnsureCreatedAsync(IServiceProvider services)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var scope = services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<LancamentosDbContext>();
                await db.Database.EnsureCreatedAsync();
                // WAL deixaria os dados no arquivo -wal e o .db pareceria vazio em visualizadores externos.
                if (db.Database.IsSqlite())
                {
                    await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=DELETE;");
                    await UpgradeSqliteAsync(db);
                }
                return;
            }
            catch when (attempt < 30)
            {
                await Task.Delay(TimeSpan.FromSeconds(2));
            }
        }
    }

    // EnsureCreated não altera bancos existentes: aplica as mudanças de schema do login em bancos SQLite antigos.
    private static async Task UpgradeSqliteAsync(LancamentosDbContext db)
    {
        await db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "Usuarios" (
                "Id" TEXT NOT NULL CONSTRAINT "PK_Usuarios" PRIMARY KEY,
                "Nome" TEXT NOT NULL,
                "Email" TEXT NOT NULL,
                "SenhaHash" TEXT NOT NULL,
                "CriadoEm" INTEGER NOT NULL);
            """);
        await db.Database.ExecuteSqlRawAsync("""CREATE UNIQUE INDEX IF NOT EXISTS "IX_Usuarios_Email" ON "Usuarios" ("Email");""");

        var colunas = await db.Database.SqlQueryRaw<string>("SELECT name AS Value FROM pragma_table_info('Lancamentos')").ToListAsync();
        if (!colunas.Contains("UsuarioId"))
            await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "Lancamentos" ADD COLUMN "UsuarioId" TEXT NOT NULL DEFAULT '00000000-0000-0000-0000-000000000000';""");
        if (!colunas.Contains("UsuarioNome"))
            await db.Database.ExecuteSqlRawAsync("""ALTER TABLE "Lancamentos" ADD COLUMN "UsuarioNome" TEXT NOT NULL DEFAULT '';""");
    }
}
