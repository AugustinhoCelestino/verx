using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using OpenTelemetry.Metrics;
using Verx.Contracts;
using Verx.Lancamentos.Api;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<LancamentosDbContext>(o =>
{
    var cs = builder.Configuration.GetConnectionString("Default");
    if (builder.Configuration["Database:Provider"] == "Sqlite") o.UseSqlite(cs); else o.UseSqlServer(cs);
});
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<LancamentoService>();
if (builder.Configuration["Messaging:Provider"] == "Http")
{
    builder.Services.Configure<HttpTransportOptions>(builder.Configuration.GetSection("Messaging:Http"));
    builder.Services.AddHttpClient("consolidado");
    builder.Services.AddSingleton<IOutboxTransportFactory, HttpTransportFactory>();
}
else
{
    builder.Services.AddSingleton(builder.Configuration.GetSection("RabbitMq").Get<RabbitOptions>() ?? new RabbitOptions());
    builder.Services.AddSingleton<IOutboxTransportFactory, RabbitTransportFactory>();
}
builder.Services.AddHostedService<OutboxPublisher>();

var jwt = builder.Configuration.GetSection("Jwt").Get<JwtSettings>() ?? new JwtSettings();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o => o.TokenValidationParameters = jwt.ValidationParameters());
builder.Services.AddAuthorization();

builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:4200"])
    .AllowAnyHeader().AllowAnyMethod()));

builder.Services.AddHealthChecks().AddDbContextCheck<LancamentosDbContext>("sqlserver");
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "Verx - LanÃ§amentos API", Version = "v1",
        Description = "Em Development, obtenha um token em POST /dev/token e informe-o em Authorize." });
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme { Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT" });
    c.AddSecurityRequirement(doc => new() { [new OpenApiSecuritySchemeReference("Bearer", doc)] = [] });
});
builder.Services.AddOpenTelemetry().WithMetrics(m => m
    .AddAspNetCoreInstrumentation()
    .AddPrometheusExporter());

var app = builder.Build();

app.UseExceptionHandler(e => e.Run(async ctx =>
{
    var ex = ctx.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
    var (status, title) = ex is DomainException de ? (400, de.Message) : (500, "Erro interno.");
    ctx.Response.StatusCode = status;
    await ctx.Response.WriteAsJsonAsync(new ProblemDetails { Title = title, Status = status });
}));
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

// Somente para demonstraÃ§Ã£o local. Em produÃ§Ã£o o token vem do IdP (ver docs/seguranca.md).
if (app.Environment.IsDevelopment())
    app.MapPost("/dev/token", () => Results.Ok(new { access_token = jwt.CreateDevToken() })).AllowAnonymous();

var api = app.MapGroup("/lancamentos").RequireAuthorization();

api.MapPost("/", async (RegistrarLancamentoRequest req, LancamentoService svc, CancellationToken ct) =>
{
    var criado = await svc.RegistrarAsync(req, ct);
    return Results.Created($"/lancamentos/{criado.Id}", criado);
});

api.MapGet("/", async ([FromQuery] DateOnly? data, LancamentoService svc, TimeProvider clock, CancellationToken ct) =>
    Results.Ok(await svc.ListarAsync(data ?? DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime), ct)));

app.Run();

public partial class Program;
