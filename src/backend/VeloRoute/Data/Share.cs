namespace VeloRoute.Data;

public sealed record Share(Guid Id, Guid RouteId, string Token, DateTimeOffset CreatedAt);
