extern alias Lanc;
extern alias Cons;

using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using LancDb = Lanc::Verx.Lancamentos.Api.Infrastructure.Persistence.LancamentosDbContext;
using Verificador = Cons::Verx.Consolidado.Api.Infrastructure.Messaging.VerificadorDeEvento;

namespace Verx.Integration.Tests;

/// <summary>
/// Sobe os dois serviços reais em memória (SQLite, modo HTTP) ligados entre si: o outbox do Lançamentos entrega ao
/// Consolidado e o Consolidado confere o evento no Lançamentos, como no modo local de produção do projeto.
/// </summary>
public sealed class Ambiente : IAsyncLifetime
{
    public const string ChaveInterna = "chave-interna-de-teste-123456";
    public const string ChaveJwt = "chave-jwt-somente-para-testes-0123456789";
    public const string AdminEmail = "demo@verx.com";
    public const string AdminSenha = "Teste@12345";

    private readonly string _dir = Directory.CreateTempSubdirectory("verx-it-").FullName;
    private WebApplicationFactory<Lanc::Program> _lanc = null!;
    private WebApplicationFactory<Cons::Program> _cons = null!;

    public HttpClient Lancamentos { get; private set; } = null!;
    public HttpClient Consolidado { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        _cons = new Fabrica<Cons::Program>(b =>
        {
            Configurar(b, "consolidado");
            b.ConfigureTestServices(s => s.AddHttpClient<Verificador>()
                .ConfigurePrimaryHttpMessageHandler(() => _lanc.Server.CreateHandler()));
        });
        _lanc = new Fabrica<Lanc::Program>(b =>
        {
            Configurar(b, "lancamentos");
            b.UseSetting("Jwt:Admins:0", AdminEmail);
            b.ConfigureTestServices(s => s.AddHttpClient("consolidado")
                .ConfigurePrimaryHttpMessageHandler(() => _cons.Server.CreateHandler()));
        });

        Consolidado = _cons.CreateClient();
        Lancamentos = _lanc.CreateClient();
        await Task.CompletedTask;
    }

    private void Configurar(IWebHostBuilder b, string nome)
    {
        b.UseEnvironment("Development");
        b.UseSetting("Database:Provider", "Sqlite");
        b.UseSetting("ConnectionStrings:Default", $"Data Source={Path.Combine(_dir, nome + ".db")}");
        b.UseSetting("Messaging:Provider", "Http");
        b.UseSetting("Messaging:Http:ApiKey", ChaveInterna);
        b.UseSetting("Jwt:Key", ChaveJwt);
        b.UseSetting("Auth:DemoSenha", AdminSenha);
    }

    public async Task DisposeAsync()
    {
        Lancamentos.Dispose();
        Consolidado.Dispose();
        await _lanc.DisposeAsync();
        await _cons.DisposeAsync();
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, true); } catch (IOException) { /* arquivo ainda em uso: o SO limpa o temp */ }
    }

    public async Task<JsonDocument> LerEventoDoOutboxAsync(Guid lancamentoId)
    {
        using var scope = _lanc.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<LancDb>();
        var payloads = await db.Outbox.AsNoTracking().Select(m => m.Payload).ToListAsync();
        var doc = payloads.Select(p => JsonDocument.Parse(p))
            .First(d => d.RootElement.GetProperty("lancamentoId").GetGuid() == lancamentoId);
        return doc;
    }

    public async Task<string> LoginAsync(string email, string senha)
    {
        var resp = await Lancamentos.PostAsJsonAsync("/auth/login", new { email, senha });
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("access_token").GetString()!;
    }

    public Task<string> LoginAdminAsync() => LoginAsync(AdminEmail, AdminSenha);

    /// <summary>Cria (como admin) e autentica um usuário sem perfil admin.</summary>
    public async Task<string> CriarUsuarioComumAsync()
    {
        var email = $"u{Guid.NewGuid():N}@verx.com";
        var r = await Enviar(HttpMethod.Post, Lancamentos, "/auth/registrar", await LoginAdminAsync(),
            new { nome = "Comum", email, senha = "Senha@12345" });
        r.EnsureSuccessStatusCode();
        return await LoginAsync(email, "Senha@12345");
    }

    public static Task<HttpResponseMessage> Enviar(HttpMethod metodo, HttpClient client, string url, string? token, object? corpo = null)
    {
        var req = new HttpRequestMessage(metodo, url);
        if (token is not null) req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (corpo is not null) req.Content = JsonContent.Create(corpo);
        return client.SendAsync(req);
    }

    private sealed class Fabrica<T>(Action<IWebHostBuilder> configurar) : WebApplicationFactory<T> where T : class
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) => configurar(builder);
    }
}
