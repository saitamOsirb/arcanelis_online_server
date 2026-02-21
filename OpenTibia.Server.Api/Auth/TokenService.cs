using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace OpenTibia.Server.Api.Auth;

public sealed class AuthOptions
{
    public string Issuer { get; init; } = "";
    public string Audience { get; init; } = "";
    public string SigningKey { get; init; } = "";
    public int TokenTtlSeconds { get; init; } = 120;
}

public interface ITokenService
{
    string CreateCharacterToken(string account, string characterName);
}

public sealed class TokenService : ITokenService
{
    private readonly AuthOptions _opt;
    private readonly byte[] _key;

    public TokenService(IOptions<AuthOptions> opt)
    {
        _opt = opt.Value;
        _key = Encoding.UTF8.GetBytes(_opt.SigningKey);
        if (_key.Length < 32)
            throw new InvalidOperationException("Auth:SigningKey debe ser >= 32 bytes (recomendado >= 64)." );
    }

    public string CreateCharacterToken(string account, string characterName)
    {
        var now = DateTimeOffset.UtcNow;

        var claims = new[]
        {
            new Claim("acct", account),
            new Claim("char", characterName),
            new Claim(JwtRegisteredClaimNames.Iat, now.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
        };

        var creds = new SigningCredentials(new SymmetricSecurityKey(_key), SecurityAlgorithms.HmacSha256);

        var jwt = new JwtSecurityToken(
            issuer: _opt.Issuer,
            audience: _opt.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: now.AddSeconds(_opt.TokenTtlSeconds).UtcDateTime,
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }
}
