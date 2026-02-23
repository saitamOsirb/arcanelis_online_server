using System.Text.Json;
using Microsoft.Extensions.Options;
using OpenTibia.Server.Api.Auth;
using OpenTibia.Server.Domain;
using OpenTibia.Server.Infrastructure;
using OpenTibia.Server.Game.Core;
using OpenTibia.Server.Game.Loop;
using OpenTibia.Server.Game.Sessions;

var builder = WebApplication.CreateBuilder(args);


builder.Services.AddSingleton<SessionRegistry>();

builder.Services.Configure<GameOptions>(builder.Configuration.GetSection("Game"));
builder.Services.AddSingleton<GameOptions>(sp => sp.GetRequiredService<IOptions<GameOptions>>().Value);

builder.Services.AddSingleton<GameServer>();
builder.Services.AddHostedService<GameLoopService>();


builder.Services.AddCors(o =>
{
    o.AddDefaultPolicy(p => p
        .SetIsOriginAllowed(_ => true)
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


builder.Services.AddSingleton<ILegacyTokenService, LegacyHmacTokenService>();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var repo = scope.ServiceProvider.GetRequiredService<IAccountRepository>();
    await repo.EnsureSchemaAsync(CancellationToken.None);
}


app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.FromSeconds(20) });
app.UseCors();

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

app.MapPost("/login-character", async (
    JsonElement body,
    IAccountRepository repo,
    ILegacyTokenService legacy,
    IConfiguration cfg,
    CancellationToken ct) =>
{
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

    var token = legacy.CreateBase64Token(name);

    var host = cfg["Server:EXTERNAL_HOST"] ?? "ws://127.0.0.1:2222";

    return Results.Ok(new { token, host });
});

app.Map("/ws", async ctx =>
{
    if (!ctx.WebSockets.IsWebSocketRequest)
    {
        ctx.Response.StatusCode = 426;
        return;
    }

    var token = ctx.Request.Query["token"].ToString();

    var legacyToken = ctx.RequestServices.GetRequiredService<ILegacyTokenService>();
    if (!legacyToken.TryValidateBase64Token(token, out var characterName))
    {
        ctx.Response.StatusCode = 401;
        return;
    }

    using var ws = await ctx.WebSockets.AcceptWebSocketAsync();

    var game = ctx.RequestServices.GetRequiredService<GameServer>();
    var session = game.CreateSession(characterName, ws);

    await session.RunAsync(ctx.RequestAborted);
});

app.Run();