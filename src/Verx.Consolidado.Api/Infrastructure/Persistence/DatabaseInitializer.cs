using Microsoft.EntityFrameworkCore;

namespace Verx.Consolidado.Api.Infrastructure.Persistence;

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
