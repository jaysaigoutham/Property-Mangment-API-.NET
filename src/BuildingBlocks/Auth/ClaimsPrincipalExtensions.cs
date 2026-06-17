using System.Security.Claims;

namespace BuildingBlocks.Auth;

public static class ClaimsPrincipalExtensions
{
    public static Guid GetUserId(this ClaimsPrincipal user)
    {
        var value = user.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? user.FindFirstValue("sub");

        return Guid.TryParse(value, out var userId) ? userId : Guid.Empty;
    }

    public static string GetEmail(this ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.Email) ?? string.Empty;

    public static string GetRole(this ClaimsPrincipal user) =>
        user.FindFirstValue(ClaimTypes.Role) ?? string.Empty;

    public static bool IsAdmin(this ClaimsPrincipal user) =>
        string.Equals(user.GetRole(), AppRoles.Admin, StringComparison.OrdinalIgnoreCase);
}
