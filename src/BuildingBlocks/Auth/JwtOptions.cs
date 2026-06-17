namespace BuildingBlocks.Auth;

public sealed class JwtOptions
{
    public const string SectionName = "Jwt";

    public string Issuer { get; set; } = "property-marketplace";
    public string Audience { get; set; } = "property-marketplace";
    public string SigningKey { get; set; } = "dev-only-signing-key-change-me-in-production-32";
    public int AccessTokenMinutes { get; set; } = 60;
    public int RefreshTokenDays { get; set; } = 14;
}
