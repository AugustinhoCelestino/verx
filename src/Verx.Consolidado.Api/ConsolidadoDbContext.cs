using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Verx.Consolidado.Api;

public sealed class ConsolidadoDbContext(DbContextOptions<ConsolidadoDbContext> options) : DbContext(options)
{
    public DbSet<SaldoDiario> Saldos => Set<SaldoDiario>();
    public DbSet<EventoProcessado> EventosProcessados => Set<EventoProcessado>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<SaldoDiario>(e =>
        {
            e.HasKey(x => x.Data);
            e.Property(x => x.TotalCreditos).HasPrecision(18, 2);
            e.Property(x => x.TotalDebitos).HasPrecision(18, 2);
            e.Property(x => x.Versao).IsConcurrencyToken();
            e.Ignore(x => x.Saldo);
        });

        b.Entity<EventoProcessado>(e => e.HasKey(x => x.EventId));

        if (Database.IsSqlite())
            foreach (var p in b.Model.GetEntityTypes().SelectMany(t => t.GetProperties())
                         .Where(p => p.ClrType == typeof(DateTimeOffset) || p.ClrType == typeof(DateTimeOffset?)))
                p.SetValueConverter(new DateTimeOffsetToBinaryConverter());
    }
}
