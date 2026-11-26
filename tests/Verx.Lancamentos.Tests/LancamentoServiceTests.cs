using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Verx.Contracts;
using Verx.Lancamentos.Api.Application;
using Verx.Lancamentos.Api.Domain;
using Verx.Lancamentos.Api.Infrastructure.Persistence;
using Verx.Lancamentos.Api.Infrastructure.Security;
using TipoLancamento = Verx.Lancamentos.Api.Domain.TipoLancamento;

namespace Verx.Lancamentos.Tests;

public sealed class LancamentoServiceTests : IAsyncLifetime
{
    private readonly SqliteConnection _conn = new("DataSource=:memory:");
    private LancamentosDbContext _db = null!;
    private readonly DateTimeOffset _now = new(2026, 11, 24, 12, 0, 0, TimeSpan.Zero);

    public async Task InitializeAsync()
    {
        await _conn.OpenAsync();
        _db = new LancamentosDbContext(new DbContextOptionsBuilder<LancamentosDbContext>().UseSqlite(_conn).Options);
        await _db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _conn.DisposeAsync();
    }

    private static readonly UsuarioAtual Usuario = new(Guid.NewGuid(), "Teste");

    private LancamentoService Service() => new(new LancamentoRepository(_db), new OutboxWriter(_db), new EfUnitOfWork(_db), new FixedClock(_now));

    private AuthService Auth() => new(new UsuarioRepository(_db), Hasher, new JwtTokenService(new JwtSettings()), new EfUnitOfWork(_db), new FixedClock(_now));

    private static readonly Pbkdf2SenhaHasher Hasher = new();

    [Fact]
    public async Task Registrar_usuario_e_login_geram_token_com_identidade_do_usuario()
    {
        var reg = await Auth().RegistrarAsync(new("Ana", "Ana@Exemplo.com ", "senha-forte-1"), default);
        var login = await Auth().LoginAsync(new("ana@exemplo.com", "senha-forte-1"), default);

        Assert.Equal(reg.Usuario.Id, login.Usuario.Id);
        var jwt = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler().ReadJwtToken(login.AccessToken);
        Assert.Equal(login.Usuario.Id.ToString(), jwt.Subject);
        Assert.Equal("Ana", jwt.Claims.First(c => c.Type == "name").Value);
        Assert.DoesNotContain("senha", jwt.RawPayload, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Login_com_senha_errada_ou_email_inexistente_falha_igualmente()
    {
        await Auth().RegistrarAsync(new("Ana", "ana@exemplo.com", "senha-forte-1"), default);

        await Assert.ThrowsAsync<CredenciaisInvalidasException>(() => Auth().LoginAsync(new("ana@exemplo.com", "errada"), default));
        await Assert.ThrowsAsync<CredenciaisInvalidasException>(() => Auth().LoginAsync(new("outro@exemplo.com", "senha-forte-1"), default));
    }

    [Theory]
    [InlineData("", "a@b.com", "senha-forte-1")]
    [InlineData("Ana", "invalido", "senha-forte-1")]
    [InlineData("Ana", "a@b.com", "curta")]
    public async Task Registrar_usuario_valida_entrada(string nome, string email, string senha) =>
        await Assert.ThrowsAsync<DomainException>(() => Auth().RegistrarAsync(new(nome, email, senha), default));

    [Fact]
    public async Task Registrar_usuario_rejeita_email_duplicado()
    {
        await Auth().RegistrarAsync(new("Ana", "ana@exemplo.com", "senha-forte-1"), default);
        await Assert.ThrowsAsync<DomainException>(() => Auth().RegistrarAsync(new("Outra", "ANA@exemplo.com", "senha-forte-2"), default));
    }

    [Fact]
    public void Hash_de_senha_usa_salt_e_verifica()
    {
        var h1 = Hasher.Gerar("abc12345");
        Assert.NotEqual(h1, Hasher.Gerar("abc12345"));
        Assert.True(Hasher.Verificar("abc12345", h1));
        Assert.False(Hasher.Verificar("abc12346", h1));
    }

    [Fact]
    public async Task Registrar_grava_lancamento_e_evento_no_outbox_juntos()
    {
        var resp = await Service().RegistrarAsync(new        (TipoLancamento.Credito, 150.50m, null, "venda"), Usuario, default);

        Assert.Equal(new DateOnly(2026, 11, 24), resp.Data);
                Assert.Equal(Usuario.Id, resp.UsuarioId);
                Assert.Equal(Usuario.Nome, resp.UsuarioNome);
        var msg = Assert.Single(_db.Outbox.ToList());
        Assert.Null(msg.PublicadoEm);
        var evento = JsonSerializer.Deserialize<LancamentoRegistrado>(msg.Payload)!;
        Assert.Equal(resp.Id, evento.LancamentoId);
        Assert.Equal(150.50m, evento.Valor);
        Assert.Equal(Verx.Contracts.TipoLancamento.Credito, evento.Tipo);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(10.123)]
    public async Task Registrar_rejeita_valor_invalido_e_nao_persiste_nada(double valor)
    {
        await Assert.ThrowsAsync<DomainException>(() =>
            Service().RegistrarAsync(new(TipoLancamento.Debito, (decimal)valor, null, null), Usuario, default));

        Assert.Empty(_db.Lancamentos);
        Assert.Empty(_db.Outbox);
    }

    [Fact]
    public async Task Registrar_rejeita_tipo_invalido()
    {
        await Assert.ThrowsAsync<DomainException>(() =>
            Service().RegistrarAsync(new((TipoLancamento)99, 10m, null, null), Usuario, default));
    }

    [Fact]
    public async Task Listar_retorna_apenas_lancamentos_da_data()
    {
        var svc = Service();
        await svc.RegistrarAsync(new(TipoLancamento.Credito, 10m,         new DateOnly(2026, 11, 1), null), Usuario, default);
                await svc.RegistrarAsync(new(TipoLancamento.Debito, 5m, new DateOnly(2026, 11, 2), null), Usuario, default);

        var lista = await svc.ListarAsync(new DateOnly(2026, 11, 1), default);

        Assert.Single(lista);
        Assert.Equal(10m, lista[0].Valor);
    }

    [Fact]
    public void Lancamento_registrado_emite_evento_de_dominio()
    {
        var l = Lancamento.Registrar(TipoLancamento.Debito, 10m, new DateOnly(2026, 11, 24), null, Guid.NewGuid(), "Ana", _now);

        var e = Assert.IsType<LancamentoRegistradoEvent>(Assert.Single(l.EventosDeDominio));
        Assert.Equal(l.Id, e.LancamentoId);
        Assert.Equal(Dinheiro.De(10m), e.Valor);
    }

    [Theory]
    [InlineData(" Ana@Exemplo.COM ", "ana@exemplo.com")]
    public void Email_e_normalizado(string entrada, string esperado) =>
        Assert.Equal(esperado, Email.Criar(entrada).Valor);

    [Fact]
    public void Email_invalido_e_rejeitado() =>
        Assert.Throws<DomainException>(() => Email.Criar("nao-e-email"));

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

