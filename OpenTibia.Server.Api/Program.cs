using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OpenTibia.Server.Api.Auth;
using OpenTibia.Server.Domain;
using OpenTibia.Server.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

// ✅ CORS (esto faltaba y causaba tu error)
builder.Services.AddCors(o =>
{
    o.AddDefaultPolicy(p => p
        .SetIsOriginAllowed(_ => true) // dev: permite todo
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials());
});

builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection("Auth"));

builder.Services.AddSingleton<IAccountRepository>(sp =>
{
    var cs = builder.Configuration.GetConnectionString("Postgres")
             ?? throw new InvalidOperationException("ConnectionStrings:Postgres requerido");
    return new AccountRepository(cs);
});
builder.Services.AddSingleton<ITokenService, TokenService>();

builder.Services.AddSingleton<OpenTibia.Server.Api.Data.CharacterTemplate>();

// JWT bearer (soporta token por querystring para /ws?token=...)
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var auth = builder.Configuration.GetSection("Auth").Get<AuthOptions>()
                   ?? throw new InvalidOperationException("Auth section requerida en appsettings.json");

        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = auth.Issuer,
            ValidAudience = auth.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(auth.SigningKey)),
            ClockSkew = TimeSpan.FromSeconds(5)
        };

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = ctx =>
            {
                var token = ctx.Request.Query["token"].ToString();
                if (!string.IsNullOrEmpty(token) && ctx.Request.Path.StartsWithSegments("/ws"))
                    ctx.Token = token;
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

// Crear schema en DB al arrancar
using (var scope = app.Services.CreateScope())
{
    var repo = scope.ServiceProvider.GetRequiredService<IAccountRepository>();
    await repo.EnsureSchemaAsync(CancellationToken.None);
}

app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20) });

// ✅ Orden correcto: CORS antes de Auth
app.UseCors();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { ok = true }));

app.MapPost("/account", async (
    CreateAccountRequest req,
    IAccountRepository repo,
    OpenTibia.Server.Api.Data.CharacterTemplate template,
    CancellationToken ct) =>
{
    if (string.IsNullOrWhiteSpace(req.Account) ||
        string.IsNullOrWhiteSpace(req.Password) ||
        string.IsNullOrWhiteSpace(req.Name))
    {
        return Results.BadRequest(new { error = "account/password/name requerido" });
    }

    // BCrypt (workFactor 12 es razonable en dev)
    var hash = BCrypt.Net.BCrypt.HashPassword(req.Password, workFactor: 12);

    var definition = JsonSerializer.Serialize(new
    {
        account = req.Account,
        createdAt = DateTimeOffset.UtcNow
    });

    var created = await repo.CreateAccountAsync(req.Account, hash, definition, ct);
    if (!created) return Results.Conflict(new { error = "Account ya existe" });

    var playerData = template.CreatePlayerJson(req.Name, req.Sex);

    var playerCreated = await repo.CreatePlayerAsync(req.Account, req.Name, playerData, ct);
    if (!playerCreated) return Results.Conflict(new { error = "Character ya existe" });

    return Results.Ok(new { ok = true });
});

app.MapPost("/characters", async (LoginRequest req, IAccountRepository repo, CancellationToken ct) =>
{
    var acc = await repo.GetAccountAsync(req.Account, ct);
    if (acc is null) return Results.Unauthorized();

    if (!BCrypt.Net.BCrypt.Verify(req.Password, acc.Value.PasswordHash))
        return Results.Unauthorized();

    var names = await repo.ListPlayersAsync(req.Account, ct);
    return Results.Ok(new CharactersResponse(names.Select(n => new CharacterDto(n)).ToList()));
});

app.MapPost("/characters/create", async (
    CreateCharacterRequest req,
    IAccountRepository repo,
    OpenTibia.Server.Api.Data.CharacterTemplate template,
    CancellationToken ct) =>
{
    var acc = await repo.GetAccountAsync(req.Account, ct);
    if (acc is null) return Results.Unauthorized();

    if (!BCrypt.Net.BCrypt.Verify(req.Password, acc.Value.PasswordHash))
        return Results.Unauthorized();

    var playerData = template.CreatePlayerJson(req.Name, req.Sex);

    var created = await repo.CreatePlayerAsync(req.Account, req.Name, playerData, ct);
    if (!created) return Results.Conflict(new { error = "Character ya existe" });

    return Results.Ok(new { ok = true });
});

app.MapPost("/login-character", async (JsonElement body, IAccountRepository repo, ITokenService tokens, CancellationToken ct) =>
{
    // Body esperado: { "account":"", "password":"", "name":"" }
    if (!body.TryGetProperty("account", out var accProp) ||
        !body.TryGetProperty("password", out var pwdProp) ||
        !body.TryGetProperty("name", out var nameProp))
    {
        return Results.BadRequest(new { error = "Body requerido: account, password, name" });
    }

    var account = accProp.GetString() ?? "";
    var password = pwdProp.GetString() ?? "";
    var name = nameProp.GetString() ?? "";

    var acc = await repo.GetAccountAsync(account, ct);
    if (acc is null) return Results.Unauthorized();

    if (!BCrypt.Net.BCrypt.Verify(password, acc.Value.PasswordHash))
        return Results.Unauthorized();

    var belongs = await repo.PlayerBelongsToAccountAsync(account, name, ct);
    if (!belongs)
        return Results.NotFound(new { error = "Character no existe o no pertenece a la cuenta" });

    var token = tokens.CreateCharacterToken(account, name);
    return Results.Ok(new TokenResponse(token));
});

// WebSocket protegido por JWT (querystring: /ws?token=...)
app.Map("/ws", async ctx =>
{
    if (!ctx.WebSockets.IsWebSocketRequest)
    {
        ctx.Response.StatusCode = 426;
        return;
    }

    // ✅ AuthenticateAsync extensión (requiere using Microsoft.AspNetCore.Authentication;)
    var auth = await ctx.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme);
    if (!auth.Succeeded || auth.Principal is null)
    {
        ctx.Response.StatusCode = 401;
        return;
    }

    var charName = auth.Principal.Claims.FirstOrDefault(c => c.Type == "char")?.Value;
    var acct = auth.Principal.Claims.FirstOrDefault(c => c.Type == "acct")?.Value;

    if (string.IsNullOrWhiteSpace(charName) || string.IsNullOrWhiteSpace(acct))
    {
        ctx.Response.StatusCode = 401;
        return;
    }

    using var ws = await ctx.WebSockets.AcceptWebSocketAsync();

    // MVP: echo loop binario
    var buffer = new byte[64 * 1024];

    while (ws.State == WebSocketState.Open && !ctx.RequestAborted.IsCancellationRequested)
    {
        var res = await ws.ReceiveAsync(buffer, ctx.RequestAborted);
        if (res.MessageType == WebSocketMessageType.Close)
            break;

        await ws.SendAsync(buffer.AsMemory(0, res.Count), WebSocketMessageType.Binary, true, ctx.RequestAborted);
    }
});

app.Run();