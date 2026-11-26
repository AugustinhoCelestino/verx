using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using OpenTelemetry.Metrics;
using Verx.Observability;
using Verx.Consolidado.Api.Application;
using Verx.Consolidado.Api.Domain;
using Verx.Consolidado.Api.Infrastructure.Caching;
using Verx.Consolidado.Api.Infrastructure.Messaging;
using Verx.Consolidado.Api.Infrastructure.Persistence;
using Verx.Consolidado.Api.Infrastructure.Security;
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
builder.Services.AddScoped<ISaldoDiarioRepository, SaldoDiarioRepository>();
builder.Services.AddScoped<IEventoProcessadoRepository, EventoProcessadoRepository>();
builder.Services.AddScoped<IUnitOfWork, EfUnitOfWork>();
builder.Services.AddSingleton<ISaldoCache, MemorySaldoCache>();
builder.Services.AddScoped<ConsolidadoService>();
var usaHttp = builder.Configuration["Messaging:Provider"] == "Http";
if (!usaHttp)
{
    builder.Services.AddSingleton(builder.Configuration.GetSection("RabbitMq").Get<RabbitOptions>() ?? new RabbitOptions());
    builder.Services.AddHostedService<LancamentoConsumer>();
}

var jwt = builder.Configuration.GetSection("Jwt").Get<JwtSettings>() ?? new JwtSettings();
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

builder.Services.AddHealthChecks().AddDbContextCheck<ConsolidadoDbContext>("database");
builder.Services.AddControllers();
builder.Services.AddObservability();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new() { Title = "Verx - Consolidado API", Version = "v1",
        Description = "Faça login em POST http://localhost:5101/auth/login (serviço de Lançamentos) e informe o access_token em Authorize." });
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

app.MapPrometheusScrapingEndpoint("/metrics");

app.MapControllers();

app.Run();

public partial class Program;
