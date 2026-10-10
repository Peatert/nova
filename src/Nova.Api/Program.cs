using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Nova.Api;
using Nova.Api.Auth;
using Nova.Api.Banking;
using Nova.Domain.Security;
using Nova.Infrastructure;
using Nova.Infrastructure.Audit;
using Nova.Infrastructure.Persistence;
using Nova.Infrastructure.Security;
using Nova.Infrastructure.Transfers;

var builder = WebApplication.CreateBuilder(args);

// ── Конфигурация и проверка секретов на старте: без ключей API не запустится ──
var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();
if (string.IsNullOrWhiteSpace(jwt.SigningKey) || Convert.FromBase64String(jwt.SigningKey).Length < 32)
    throw new InvalidOperationException("Jwt:SigningKey не задан или короче 32 байт (base64).");
var encryptionKey = builder.Configuration["Nova:EncryptionKey"]
    ?? throw new InvalidOperationException("Nova:EncryptionKey не задан.");

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(new FieldEncryptor(encryptionKey));
builder.Services.AddNovaInfrastructure(builder.Configuration);
builder.Services.AddScoped<AuditWriter>();
builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<TransferService>();

builder.Services.ConfigureHttpJsonOptions(o =>
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

// ── Аутентификация: JWT ──
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.MapInboundClaims = false; // claims читаются под теми же именами, под которыми записаны
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = TokenService.SigningKey(jwt),
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = NovaClaims.Name,
            RoleClaimType = NovaClaims.Role,
        };
    });

// ── Авторизация: одна политика на каждое право. Эндпоинт требует право, а не роль ──
builder.Services.AddAuthorization(o =>
{
    foreach (var permission in Permissions.All())
        o.AddPolicy(permission, p => p.RequireAuthenticatedUser().RequireClaim(NovaClaims.Permission, permission));
    o.AddPolicy("client", p => p.RequireAuthenticatedUser().RequireClaim(NovaClaims.SubjectType, "client"));
});

// ── Ограничение частоты запросов к /api/auth с одного IP (в проде 10 в минуту) ──
var authPerMinute = builder.Configuration.GetValue("RateLimit:AuthPerMinute", 10);
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.AddPolicy("auth", ctx => RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = authPerMinute, Window = TimeSpan.FromMinutes(1) }));
});

builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks().AddDbContextCheck<NovaDbContext>("postgres");

var app = builder.Build();

app.UseExceptionHandler();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi(); // /openapi/v1.json

    // В разработке накатываем миграции при старте. В проде — отдельным шагом деплоя.
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<NovaDbContext>();
    await db.Database.MigrateAsync();
}

app.MapHealthChecks("/health");
app.MapAuthEndpoints();
app.MapMeEndpoints();
app.MapSystemEndpoints();
app.MapBankingEndpoints();

app.Run();

// Нужно integration-тестам: WebApplicationFactory<Program> запускает API целиком.
public partial class Program { }
