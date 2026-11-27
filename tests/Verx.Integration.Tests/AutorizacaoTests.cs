extern alias Lanc;
extern alias Cons;

using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;

namespace Verx.Integration.Tests;

public sealed class AutorizacaoTests(Ambiente a) : IClassFixture<Ambiente>
{
    private static Task<HttpResponseMessage> Enviar(HttpMethod m, HttpClient c, string url, string? token, object? corpo = null) =>
        Ambiente.Enviar(m, c, url, token, corpo);

    private static object NovoUsuario() => new { nome = "Novo", email = $"n{Guid.NewGuid():N}@verx.com", senha = "Senha@12345" };

    [Fact]
    public async Task Cadastro_anonimo_e_recusado()
    {
        var r = await Enviar(HttpMethod.Post, a.Lancamentos, "/auth/registrar", null, NovoUsuario());
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Fact]
    public async Task Cadastro_por_usuario_comum_e_proibido()
    {
        var token = await a.CriarUsuarioComumAsync();
        var r = await Enviar(HttpMethod.Post, a.Lancamentos, "/auth/registrar", token, NovoUsuario());
        Assert.Equal(HttpStatusCode.Forbidden, r.StatusCode);
    }

    [Fact]
    public async Task Cadastro_por_admin_cria_usuario_que_consegue_entrar()
    {
        var email = $"ok{Guid.NewGuid():N}@verx.com";
        var r = await Enviar(HttpMethod.Post, a.Lancamentos, "/auth/registrar", await a.LoginAdminAsync(),
            new { nome = "Ok", email, senha = "Senha@12345" });
        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        Assert.False(string.IsNullOrEmpty(await a.LoginAsync(email, "Senha@12345")));
    }

    [Theory]
    [InlineData("lancamentos", "/lancamentos")]
    [InlineData("lancamentos", "/logs")]
    [InlineData("consolidado", "/consolidado/2030-01-01")]
    [InlineData("consolidado", "/consolidado?de=2030-01-01&ate=2030-01-02")]
    [InlineData("consolidado", "/logs")]
    public async Task Leitura_sem_token_retorna_401(string servico, string url)
    {
        var r = await a.Cliente(servico).GetAsync(url);
        Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
    }

    [Theory]
    [InlineData("lancamentos")]
    [InlineData("consolidado")]
    public async Task Logs_exigem_perfil_admin(string servico)
    {
        var comum = await a.CriarUsuarioComumAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await Enviar(HttpMethod.Get, a.Cliente(servico), "/logs", comum)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Enviar(HttpMethod.Get, a.Cliente(servico), "/logs", await a.LoginAdminAsync())).StatusCode);
    }

    [Fact]
    public async Task Usuario_comum_le_saldo_mas_nao_administra_a_dlq()
    {
        var comum = await a.CriarUsuarioComumAsync();
        Assert.Equal(HttpStatusCode.OK, (await Enviar(HttpMethod.Get, a.Consolidado, "/consolidado/2030-01-01", comum)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Enviar(HttpMethod.Post, a.Consolidado, "/admin/dlq/reprocessar", comum)).StatusCode);
    }

    [Fact]
    public async Task Token_assinado_com_outra_chave_nao_concede_admin()
    {
        var chave = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("outra-chave-qualquer-com-mais-de-32-bytes!!"));
        var forjado = new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken("verx", "verx-api",
            [new Claim("sub", Guid.NewGuid().ToString()), new Claim("name", "x"), new Claim("role", "admin")],
            expires: DateTime.UtcNow.AddHours(1), signingCredentials: new SigningCredentials(chave, SecurityAlgorithms.HmacSha256)));

        Assert.Equal(HttpStatusCode.Unauthorized, (await Enviar(HttpMethod.Get, a.Lancamentos, "/logs", forjado)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Enviar(HttpMethod.Get, a.Consolidado, "/logs", forjado)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Enviar(HttpMethod.Post, a.Lancamentos, "/auth/registrar", forjado, NovoUsuario())).StatusCode);
    }

    [Fact]
    public async Task Rotas_internas_exigem_a_chave()
    {
        var evento = new { eventId = Guid.NewGuid(), lancamentoId = Guid.NewGuid(), tipo = 1, valor = 10m, data = "2030-02-01", ocorridoEm = DateTimeOffset.UtcNow };

        var semChave = await a.Consolidado.PostAsJsonAsync("/internal/eventos", evento);
        Assert.Equal(HttpStatusCode.Unauthorized, semChave.StatusCode);

        var chaveErrada = new HttpRequestMessage(HttpMethod.Get, $"/internal/eventos/{evento.eventId}");
        chaveErrada.Headers.Add("X-Api-Key", "errada");
        Assert.Equal(HttpStatusCode.Unauthorized, (await a.Lancamentos.SendAsync(chaveErrada)).StatusCode);
    }

    [Fact]
    public async Task Chave_interna_nao_e_um_token_de_usuario()
    {
        var req = new HttpRequestMessage(HttpMethod.Get, "/lancamentos");
        req.Headers.Add("X-Api-Key", Ambiente.ChaveInterna);
        Assert.Equal(HttpStatusCode.Unauthorized, (await a.Lancamentos.SendAsync(req)).StatusCode);
    }
}

public static class AmbienteExtensoes
{
    public static HttpClient Cliente(this Ambiente a, string servico) => servico == "lancamentos" ? a.Lancamentos : a.Consolidado;
}
