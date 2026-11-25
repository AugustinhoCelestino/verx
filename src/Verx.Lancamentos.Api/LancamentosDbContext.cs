using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Verx.Lancamentos.Api;

public sealed class LancamentosDbContext(DbContextOptions<LancamentosDbContext> options) : DbContext(options)
{
    public DbSet<Lancamento> Lancamentos => Set<Lancamento>();
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Lancamento>(e =>
        {
            e.HasKey(x => x.Id);
            e.Property(x => x.Valor).HasPrecision(18, 2);
            e.Property(x => x.Descricao).HasMaxLength(200);
            e.HasIndex(x => x.Data);
        });

        b.Entity<OutboxMessage>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.PublicadoEm);
        });

        // SQLite nÃ£o ordena/compara DateTimeOffset nativamente.
        if (Database.IsSqlite())
            foreach (var p in b.Model.GetEntityTypes().SelectMany(t => t.GetProperties())
                         .Where(p => p.ClrType == typeof(DateTimeOffset) || p.ClrType == typeof(DateTimeOffset?)))
                p.SetValueConverter(new DateTimeOffsetToBinaryConverter());
    }
}
