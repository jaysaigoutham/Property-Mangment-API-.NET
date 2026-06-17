using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace BuildingBlocks.Auth;

public sealed record TokenResponse(
    string AccessToken,
    DateTimeOffset ExpiresAtUtc,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAtUtc);

public interface IJwtTokenService
{
    TokenResponse Create(Guid userId, string email, string role, string displayName);
    string CreateRefreshToken();
    string HashRefreshToken(string refreshToken);
    bool VerifyRefreshToken(string refreshToken, string refreshTokenHash);
}

public sealed class JwtTokenService(IOptions<JwtOptions> options, TimeProvider timeProvider) : IJwtTokenService
{
    private readonly JwtOptions _options = options.Value;

    public TokenResponse Create(Guid userId, string email, string role, string displayName)
    {
        var now = timeProvider.GetUtcNow();
        var expiresAt = now.AddMinutes(_options.AccessTokenMinutes);
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
            new Claim(JwtRegisteredClaimNames.Email, email),
            new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
            new Claim(ClaimTypes.Email, email),
            new Claim(ClaimTypes.Name, displayName),
            new Claim(ClaimTypes.Role, role)
        };

        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now.UtcDateTime,
            expires: expiresAt.UtcDateTime,
            signingCredentials: credentials);

        var refreshToken = CreateRefreshToken();

        return new TokenResponse(
            new JwtSecurityTokenHandler().WriteToken(token),
            expiresAt,
            refreshToken,
            now.AddDays(_options.RefreshTokenDays));
    }

    public string CreateRefreshToken()
    {
        Span<byte> bytes = stackalloc byte[64];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToBase64String(bytes);
    }

    public string HashRefreshToken(string refreshToken)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(refreshToken));
        return Convert.ToHexString(hash);
    }

    public bool VerifyRefreshToken(string refreshToken, string refreshTokenHash) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(HashRefreshToken(refreshToken)),
            Encoding.UTF8.GetBytes(refreshTokenHash));
}
