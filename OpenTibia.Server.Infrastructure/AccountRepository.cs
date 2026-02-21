using System.Data;
using Dapper;
using Npgsql;

namespace OpenTibia.Server.Infrastructure;

public interface IAccountRepository
{
    Task EnsureSchemaAsync(CancellationToken ct);

    Task<bool> CreateAccountAsync(string account, string passwordHash, string definitionJson, CancellationToken ct);
    Task<(string PasswordHash, string DefinitionJson)?> GetAccountAsync(string account, CancellationToken ct);

    Task<bool> CreatePlayerAsync(string account, string name, string dataJson, CancellationToken ct);
    Task<IReadOnlyList<string>> ListPlayersAsync(string account, CancellationToken ct);
    Task<bool> PlayerBelongsToAccountAsync(string account, string name, CancellationToken ct);
}

public sealed class AccountRepository : IAccountRepository
{
    private readonly string _cs;
    public AccountRepository(string connectionString) => _cs = connectionString;

    private async Task<IDbConnection> OpenAsync(CancellationToken ct)
    {
        var conn = new NpgsqlConnection(_cs);
        await conn.OpenAsync(ct);
        return conn;
    }

    public async Task EnsureSchemaAsync(CancellationToken ct)
    {
        const string sql = """
        CREATE TABLE IF NOT EXISTS accounts (
            account TEXT PRIMARY KEY,
            hash TEXT NOT NULL,
            definition JSONB NOT NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );

        CREATE TABLE IF NOT EXISTS players (
            name TEXT PRIMARY KEY,
            account TEXT NOT NULL REFERENCES accounts(account) ON DELETE CASCADE,
            data JSONB NOT NULL,
            created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
            updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
        );

        CREATE INDEX IF NOT EXISTS idx_players_account ON players(account);
        """;

        using var conn = await OpenAsync(ct);
        await conn.ExecuteAsync(new CommandDefinition(sql, cancellationToken: ct));
    }

    public async Task<bool> CreateAccountAsync(string account, string passwordHash, string definitionJson, CancellationToken ct)
    {
        const string sql = """
        INSERT INTO accounts(account, hash, definition)
        VALUES (@account, @hash, CAST(@def AS JSONB))
        ON CONFLICT (account) DO NOTHING;
        """;

        using var conn = await OpenAsync(ct);
        var rows = await conn.ExecuteAsync(new CommandDefinition(sql,
            new { account, hash = passwordHash, def = definitionJson }, cancellationToken: ct));
        return rows == 1;
    }

    public async Task<(string PasswordHash, string DefinitionJson)?> GetAccountAsync(string account, CancellationToken ct)
    {
        const string sql = "SELECT hash as PasswordHash, definition::text as DefinitionJson FROM accounts WHERE account=@account;";
        using var conn = await OpenAsync(ct);
        return await conn.QuerySingleOrDefaultAsync<(string, string)?>(
            new CommandDefinition(sql, new { account }, cancellationToken: ct));
    }

    public async Task<bool> CreatePlayerAsync(string account, string name, string dataJson, CancellationToken ct)
    {
        const string sql = """
        INSERT INTO players(name, account, data)
        VALUES (@name, @account, CAST(@data AS JSONB))
        ON CONFLICT (name) DO NOTHING;
        """;

        using var conn = await OpenAsync(ct);
        var rows = await conn.ExecuteAsync(new CommandDefinition(sql,
            new { name, account, data = dataJson }, cancellationToken: ct));
        return rows == 1;
    }

    public async Task<IReadOnlyList<string>> ListPlayersAsync(string account, CancellationToken ct)
    {
        const string sql = "SELECT name FROM players WHERE account=@account ORDER BY name;";
        using var conn = await OpenAsync(ct);
        var rows = await conn.QueryAsync<string>(new CommandDefinition(sql, new { account }, cancellationToken: ct));
        return rows.ToList();
    }

    public async Task<bool> PlayerBelongsToAccountAsync(string account, string name, CancellationToken ct)
    {
        const string sql = "SELECT 1 FROM players WHERE account=@account AND name=@name LIMIT 1;";
        using var conn = await OpenAsync(ct);
        var v = await conn.ExecuteScalarAsync<int?>(new CommandDefinition(sql, new { account, name }, cancellationToken: ct));
        return v.HasValue;
    }
}
