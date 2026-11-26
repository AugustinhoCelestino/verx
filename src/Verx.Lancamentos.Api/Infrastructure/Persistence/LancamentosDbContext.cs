using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Verx.Lancamentos.Api.Application;
using Verx.Lancamentos.Api.Domain;

namespace Verx.Lancamentos.Api.Infrastructure.Persistence;

/// <summary>Transactional outbox: o evento é gravado na mesma transação do lançamento.</summary>
public sealed class OutboxMessage
{
    public Guid Id { get; set; }
    public string Payload { get; set; } = string.Empty;
    public DateTimeOffset CriadoEm { get; set; }
    public DateTimeOffset? PublicadoEm { get; set; }
    public int Tentativas { get; set; }
}

public sealed class LancamentosDbContext(DbContextOptions<LancamentosDbContext> options) : DbContext(options)
{
    public DbSet<Lancamento> Lancamentos => Set<Lancamento>();
    public DbSet<OutboxMessage> Outbox => Set<OutboxMessage>();
    public DbSet<Usuario> Usuarios => Set<Usuario>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<Lancamento>(e =>
        {
            e.HasKey(x => x.Id);
            e.Ignore(x => x.EventosDeDominio);
            // Value object persistido na mesma coluna decimal de antes.
            e.Property(x => x.Valor).HasConversion(v => v.Valor, v => Dinheiro.De(v)).HasPrecision(18, 2);
            e.Property(x => x.Descricao).HasMaxLength(Lancamento.TamanhoMaximoDescricao);
            e.Property(x => x.UsuarioNome).HasMaxLength(Usuario.TamanhoMaximoNome);
            e.HasIndex(x => x.Data);
        });

        b.Entity<Usuario>(e =>
        {
            e.HasKey(x => x.Id);
            e.Ignore(x => x.EventosDeDominio);
            e.Property(x => x.Nome).HasMaxLength(Usuario.TamanhoMaximoNome);
            e.Property(x => x.Email).HasConversion(v => v.Valor, v => Email.Criar(v)).HasMaxLength(Email.TamanhoMaximo);
            e.HasIndex(x => x.Email).IsUnique();
        });

        b.Entity<OutboxMessage>(e =>
        {
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.PublicadoEm);
        });

        // SQLite não ordena/compara DateTimeOffset nativamente.
        if (Database.IsSqlite())
            foreach (var p in b.Model.GetEntityTypes().SelectMany(t => t.GetProperties())
                         .Where(p => p.ClrType == typeof(DateTimeOffset) || p.ClrType == typeof(DateTimeOffset?)))
                p.SetValueConverter(new DateTimeOffsetToBinaryConverter());
    }
}
