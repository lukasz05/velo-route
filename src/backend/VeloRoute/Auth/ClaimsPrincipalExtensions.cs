using System.Security.Claims;

namespace VeloRoute.Auth;

public static class ClaimsPrincipalExtensions
{
    public static string? GetSub(this ClaimsPrincipal user) =>
        user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? user.FindFirst("sub")?.Value;
}
