using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Nova.Admin.Components;
using Nova.Domain.Security;
using Nova.Infrastructure;
using Nova.Infrastructure.Audit;
using Nova.Infrastructure.Security;

// Консоль владельца и сотрудников банка.
// Blazor со статическим серверным рендерингом: страницы — обычный HTML, без WebSocket и JavaScript.
// Данные читаются напрямую из БД через Nova.Infrastructure; доступ к страницам — по правам роли.

var builder = WebApplication.CreateBuilder(args);

var encryptionKey = builder.Configuration["Nova:EncryptionKey"]
    ?? throw new InvalidOperationException("Nova:EncryptionKey не задан.");

builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(new FieldEncryptor(encryptionKey));
builder.Services.AddNovaInfrastructure(builder.Configuration);
builder.Services.AddScoped<AuditWriter>();

builder.Services.AddRazorComponents();
builder.Services.AddCascadingAuthenticationState();

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(o =>
    {
        o.LoginPath = "/login";
        o.AccessDeniedPath = "/denied";
        o.ExpireTimeSpan = TimeSpan.FromMinutes(30); // консоль владельца не держит сессию сутками
        o.SlidingExpiration = true;
        o.Cookie.Name = "nova.admin";
        o.Cookie.HttpOnly = true;
        o.Cookie.SameSite = SameSiteMode.Strict;
        o.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
    });

builder.Services.AddAuthorization(o =>
{
    foreach (var permission in Permissions.All())
        o.AddPolicy(permission, p => p.RequireAuthenticatedUser().RequireClaim("perm", permission));
});

var app = builder.Build();

app.UseExceptionHandler("/error", createScopeForErrors: true);
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.UseAntiforgery();

app.MapPost("/logout", async (HttpContext ctx) =>
{
    await ctx.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    return Results.Redirect("/login");
});

app.MapRazorComponents<App>();

app.Run();
