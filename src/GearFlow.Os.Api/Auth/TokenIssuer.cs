using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using GearFlow.Os.Api.Jwt;
using Microsoft.IdentityModel.Tokens;

namespace GearFlow.Os.Api.Auth;

public sealed record IssuedToken(string AccessToken, string TokenType, int ExpiresIn, DateTime ExpiresAtUtc);

public static class TokenIssuer
{
    /// <summary>Emite um JWT HS256 com o mesmo issuer, audience e chave que a API valida.</summary>
    public static IssuedToken Issue(JwtOptions jwt, string subject, TimeSpan lifetime, DateTime nowUtc)
    {
        var expiresAt = nowUtc.Add(lifetime);
        var token = new JwtSecurityToken(
            jwt.Issuer,
            jwt.Audience,
            [
                new Claim(JwtRegisteredClaimNames.Sub, subject),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
                new Claim("actor_type", "staff")
            ],
            notBefore: nowUtc,
            expires: expiresAt,
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Secret)),
                SecurityAlgorithms.HmacSha256));

        return new IssuedToken(
            new JwtSecurityTokenHandler().WriteToken(token),
            "Bearer",
            (int)lifetime.TotalSeconds,
            expiresAt);
    }
}
