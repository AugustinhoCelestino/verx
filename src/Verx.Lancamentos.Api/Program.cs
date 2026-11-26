using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using OpenTelemetry.Metrics;
using Verx.Contracts;
using Verx.Lancamentos.Api.Application;
using Verx.Lancamentos.Api.Domain;
using Verx.Lancamentos.Api.Endpoints;
using Verx.Lancamentos.Api.Infrastructure.Messaging;
using Verx.Lancamentos.Api.Infrastructure.Persistence;
using Verx.Lancamentos.Api.Infrastructure.Security;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<LancamentosDbContext>(o =>
{
    var cs = builder.Configuration.GetConnectionString("Default");
    if (builder.Configuration["Database:Provider"] == "Sqlite") o.UseSqlite(cs); else o.UseSqlServer(cs);
});
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddScoped<ILancamentoRepository, LancamentoRepository>();
builder.Services.AddScoped<IUsuarioRepository, UsuarioRepository>();
builder.Services.AddScoped<IOutbox, OutboxWriter>();
builder.Services.AddScoped<IUnitOfWork, EfUnitOfWork>();
builder.Services.AddSingleton<ISenhaHasher, Pbkdf2SenhaHasher>();
builder.Services.AddSingleton<ITokenService, JwtTokenService>();
builder.Services.AddScoped<LancamentoService>();
builder.Services.AddScoped<AuthService>();
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
builder.Services.AddSingleton(jwt);
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.MapInboundClaims = false;
        o.TokenValidationParameters = jwt.ValidationParameters();
    });
builder.Services.AddAuthorization();

builder.Services.AddCors(o => o.AddDefaultPolicy(p => p
    .WithOrigins(builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? ["http://localhost:4200"])
    .AllowAnyHeader().AllowAnyMethod()));

builder.Services.AddHealthChecks().AddDbContextCheck<LancamentosDbContext>("sqlserver");
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "Verx - Lançamentos API", Version = "v1",
        Description = "Crie uma conta em POST /auth/registrar (ou use o usuário demo em Development), faça POST /auth/login e informe o access_token em Authorize." });
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
    var (status, title) = ex switch
    {
        DomainException de => (400, de.Message),
        CredenciaisInvalidasException ci => (401, ci.Message),
        _ => (500, "Erro interno.")
    };
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

// Usuário de demonstração para o ambiente local (nunca em produção).
if (app.Environment.IsDevelopment() && app.Configuration.GetValue("Auth:SeedDemoUser", true)
    && app.Configuration.GetValue("Database:AutoCreate", true))
{
    using var scope = app.Services.CreateScope();
    var auth = scope.ServiceProvider.GetRequiredService<AuthService>();
    try { await auth.RegistrarAsync(new("Comerciante Demo", "demo@verx.com", "Demo@12345"), default); }
    catch (DomainException) { /* já existe */ }
}

app.MapAuth();
app.MapLancamentos();

app.Run();

public partial class Program;
