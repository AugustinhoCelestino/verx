using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Verx.Lancamentos.Api.Application;

namespace Verx.Lancamentos.Api.Endpoints;

public static class UsuarioClaims
{
    public static UsuarioAtual? Atual(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirst("sub")?.Value, out var id)
            ? new UsuarioAtual(id, user.FindFirst("name")?.Value ?? "")
            : null;
}

public static class ApiEndpoints
{
    public static void MapAuth(this WebApplication app)
    {
        var authApi = app.MapGroup("/auth");
        authApi.MapPost("/registrar", async (RegistrarUsuarioRequest req, AuthService svc, CancellationToken ct) =>
            Results.Created("/auth/me", await svc.RegistrarAsync(req, ct))).AllowAnonymous();
        authApi.MapPost("/login", async (LoginRequest req, AuthService svc, CancellationToken ct) =>
            Results.Ok(await svc.LoginAsync(req, ct))).AllowAnonymous();
        authApi.MapGet("/me", async (ClaimsPrincipal user, AuthService svc, CancellationToken ct) =>
            user.Atual() is { } u && await svc.ObterAsync(u.Id, ct) is { } resp ? Results.Ok(resp) : Results.Unauthorized())
            .RequireAuthorization();
    }

    public static void MapLancamentos(this WebApplication app)
    {
        var api = app.MapGroup("/lancamentos").RequireAuthorization();

        api.MapPost("/", async (RegistrarLancamentoRequest req, ClaimsPrincipal user, LancamentoService svc, CancellationToken ct) =>
        {
            if (user.Atual() is not { } usuario) return Results.Unauthorized();
            var criado = await svc.RegistrarAsync(req, usuario, ct);
            return Results.Created($"/lancamentos/{criado.Id}", criado);
        });

        api.MapGet("/", async ([FromQuery] DateOnly? data, LancamentoService svc, TimeProvider clock, CancellationToken ct) =>
            Results.Ok(await svc.ListarAsync(data ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime), ct)));
    }
}
