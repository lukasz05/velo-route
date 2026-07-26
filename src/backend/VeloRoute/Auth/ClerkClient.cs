using System.Net;
using Microsoft.Extensions.Logging;

namespace VeloRoute.Auth;

internal sealed class ClerkClient(HttpClient httpClient, ILogger<ClerkClient> logger) : IClerkClient
{
    public async Task<bool> DeleteUserAsync(string clerkUserId, CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await httpClient.DeleteAsync($"users/{clerkUserId}", cancellationToken);

            if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound)
                return true;

            logger.LogWarning(
                "Clerk user deletion failed for {ClerkUserId} with status {StatusCode}",
                clerkUserId, response.StatusCode);
            return false;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning(ex, "Clerk user deletion threw for {ClerkUserId}", clerkUserId);
            return false;
        }
    }
}
