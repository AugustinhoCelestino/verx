using System.Text.Json.Serialization;
using Verx.Lancamentos.Api.Domain;

namespace Verx.Lancamentos.Api.Application;

public sealed record UsuarioAtual(Guid Id, string Nome);

public sealed record RegistrarLancamentoRequest(TipoLancamento Tipo, decimal Valor, DateOnly? Data, string? Descricao);

public sealed record LancamentoResponse(Guid Id, TipoLancamento Tipo, decimal Valor, DateOnly Data, string? Descricao, DateTimeOffset CriadoEm, Guid UsuarioId, string UsuarioNome)
{
    public static LancamentoResponse From(Lancamento l) =>
        new(l.Id, l.Tipo, l.Valor.Valor, l.Data, l.Descricao, l.CriadoEm, l.UsuarioId, l.UsuarioNome);
}

public sealed record RegistrarUsuarioRequest(string? Nome, string? Email, string? Senha);

public sealed record LoginRequest(string? Email, string? Senha);

public sealed record UsuarioResponse(Guid Id, string Nome, string Email);

public sealed record LoginResponse(
    [property: JsonPropertyName("access_token")] string AccessToken,
    [property: JsonPropertyName("token_type")] string TokenType,
    [property: JsonPropertyName("expires_in")] int ExpiresIn,
    UsuarioResponse Usuario);
