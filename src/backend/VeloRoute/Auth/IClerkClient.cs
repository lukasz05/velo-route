namespace VeloRoute.Auth;

public interface IClerkClient
{
    Task<bool> DeleteUserAsync(string clerkUserId, CancellationToken cancellationToken = default);
}
