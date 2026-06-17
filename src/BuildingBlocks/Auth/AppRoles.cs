namespace BuildingBlocks.Auth;

public static class AppRoles
{
    public const string Buyer = "buyer";
    public const string Agent = "agent";
    public const string Admin = "admin";

    public static readonly string[] All = [Buyer, Agent, Admin];

    public static bool IsKnown(string role) =>
        All.Contains(role, StringComparer.OrdinalIgnoreCase);

    public static string Normalize(string role) =>
        role.Trim().ToLowerInvariant();
}
