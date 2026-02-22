using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace OpenTibia.Server.Api.Auth;

public interface ILegacyTokenService
{
    string CreateBase64Token(string name);
    bool TryValidateBase64Token(string tokenBase64, out string name);
}

public sealed class LegacyHmacTokenService : ILegacyTokenService
{
    private readonly string _secret;

    public LegacyHmacTokenService(IConfiguration cfg)
    {
        _secret = cfg["HMAC:SHARED_SECRET"]
                  ?? throw new InvalidOperationException("Falta HMAC:SHARED_SECRET en appsettings.json");
    }

    public string CreateBase64Token(string name)
    {
        // Legacy EXACTO: 3000 ms
        long expire = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() + 3000;

        string sigHex = ComputeHexHmac(name + expire);

        var payload = new
        {
            name,
            expire,
            token = sigHex
        };

        var json = JsonSerializer.Serialize(payload);
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    public bool TryValidateBase64Token(string tokenBase64, out string name)
    {
        name = "";
        if (string.IsNullOrWhiteSpace(tokenBase64)) return false;

        // 1) normaliza: URL decode y problemas comunes
        tokenBase64 = tokenBase64.Trim();

        // Si llega URL-encoded (%3D etc), lo reparamos:
        tokenBase64 = Uri.UnescapeDataString(tokenBase64);

        // Si '+' fue convertido a espacio
        tokenBase64 = tokenBase64.Replace(' ', '+');

        // 2) padding base64 si falta
        int mod = tokenBase64.Length % 4;
        if (mod != 0)
            tokenBase64 += new string('=', 4 - mod);

        string json;
        try
        {
            json = Encoding.UTF8.GetString(Convert.FromBase64String(tokenBase64));
        }
        catch
        {
            return false;
        }

        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch { return false; }

        if (!doc.RootElement.TryGetProperty("name", out var nameProp)) return false;
        if (!doc.RootElement.TryGetProperty("expire", out var expProp)) return false;
        if (!doc.RootElement.TryGetProperty("token", out var tokProp)) return false;

        var n = nameProp.GetString();
        if (string.IsNullOrWhiteSpace(n)) return false;

        long expire = expProp.GetInt64();
        var tokenHex = tokProp.GetString();
        if (string.IsNullOrWhiteSpace(tokenHex)) return false;

        long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (expire <= now) return false;

        var expected = ComputeHexHmac(n + expire);
        if (!FixedTimeEqualsAscii(tokenHex!, expected)) return false;

        name = n!;
        return true;
    }

    private string ComputeHexHmac(string message)
    {
        using var h = new HMACSHA256(Encoding.UTF8.GetBytes(_secret));
        var bytes = h.ComputeHash(Encoding.UTF8.GetBytes(message));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static bool FixedTimeEqualsAscii(string a, string b)
    {
        var ba = Encoding.ASCII.GetBytes(a);
        var bb = Encoding.ASCII.GetBytes(b);
        return CryptographicOperations.FixedTimeEquals(ba, bb);
    }
}