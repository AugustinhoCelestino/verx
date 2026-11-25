using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using OpenTelemetry.Metrics;
using Verx.Consolidado.Api;
using Verx.Contracts;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<ConsolidadoDbContext>(o =>
{
    var cs = builder.Configuration.GetConnectionString("Default");
    if (builder.Configuration["Database:Provider"] == "Sqlite") o.UseSqlite(cs); else o.UseSqlServer(cs);
});
builder.Services.AddMemoryCache();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(builder.Configuration.GetSection("Cache").Get<ConsolidadoOptions>() ?? new ConsolidadoOptions());
builder.Services.AddScoped<ConsolidadoService>();
var usaHttp = builder.Configuration["Messaging:Provider"] == "Http";
if (!usaHttp)
{
    builder.Services.AddSingleton(builder.Configuration.GetSection("RabbitMq").Get<RabbitOptions>() ?? new RabbitOptions());
    builder.Services.AddHostedService<LancamentoConsumer>();
}

var jwt = builder.Configuration.GetSection("Jwt").Get<JwtSettings>() ?? new JwtSettings();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o => o.TokenValidationParameters = jwt.ValidationParameters());
builder.Services.AddAuthorization();

builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:4200"])
    .AllowAnyHeader().AllowAnyMethod()));

builder.Services.AddHealthChecks().AddDbContextCheck<ConsolidadoDbContext>("sqlserver");
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "Verx - Consolidado API", Version = "v1",
        Description = "Em Development, obtenha um token em POST http://localhost:5101/dev/token (serviÃ§o de LanÃ§amentos) e informe-o em Authorize." });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme { Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT" });
    c.AddSecurityRequirement(doc => new() { [new OpenApiSecuritySchemeReference("Bearer", doc)] = [] });
});
builder.Services.AddOpenTelemetry().WithMetrics(m => m
    .AddAspNetCoreInstrumentation()
    .AddPrometheusExporter());

var app = builder.Build();

app.UseCors();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(o => o.RoutePrefix = "swagger");
}
app.UseAuthentication();
app.UseAuthorization();

if (app.Configuration.GetValue("Database:AutoCreate", true))
    await DatabaseInitializer.EnsureCreatedAsync(app.Services);

app.MapHealthChecks("/health/live", new() { Predicate = _ => false });
app.MapHealthChecks("/health/ready");
app.MapPrometheusScrapingEndpoint("/metrics");

var api = app.MapGroup("/consolidado").RequireAuthorization();

// Modo local (sem broker): recebe eventos do serviÃ§o de LanÃ§amentos por HTTP, protegido por chave interna.
if (usaHttp)
{
    var chave = System.Text.Encoding.UTF8.GetBytes(app.Configuration["Messaging:Http:ApiKey"] ?? "dev-only-internal-key");
    app.MapPost("/internal/eventos", async (HttpContext ctx, LancamentoRegistrado evento, ConsolidadoService svc, CancellationToken ct) =>
    {
        var recebida = System.Text.Encoding.UTF8.GetBytes(ctx.Request.Headers["X-Api-Key"].ToString());
        if (!System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(recebida, chave))
            return Results.Unauthorized();
        await svc.ProcessarAsync(evento, ct);
        return Results.Accepted();
    }).AllowAnonymous();
}

api.MapGet("/{data}", async (DateOnly data, ConsolidadoService svc, CancellationToken ct) =>
    Results.Ok(await svc.ObterAsync(data, ct)));

api.MapGet("/", async ([FromQuery] DateOnly de, [FromQuery] DateOnly ate, ConsolidadoService svc, CancellationToken ct) =>
    de > ate
        ? Results.BadRequest(new ProblemDetails { Title = "'de' deve ser anterior ou igual a 'ate'.", Status = 400 })
        : Results.Ok(await svc.ListarPeriodoAsync(de, ate, ct)));

app.Run();

public partial class Program;
