using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace Verx.Consolidado.Api;

public sealed class JwtSettings
{
    public string Issuer { get; set; } = "verx";
    public string Audience { get; set; } = "verx-api";
    public string Key { get; set; } = "dev-only-signing-key-change-me-32bytes!!";

    public TokenValidationParameters ValidationParameters() => new()
    {
        ValidIssuer = Issuer,
        ValidAudience = Audience,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Key)),
        ValidateIssuerSigningKey = true,
        NameClaimType = "name"
    };
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
                var db = scope.ServiceProvider.GetRequiredService<ConsolidadoDbContext>();
                await db.Database.EnsureCreatedAsync();
                // WAL deixaria os dados no arquivo -wal e o .db pareceria vazio em visualizadores externos.
                if (db.Database.IsSqlite()) await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=DELETE;");
                return;
            }
            catch when (attempt < 30)
            {
                await Task.Delay(TimeSpan.FromSeconds(2));
            }
        }
    }
}
