namespace VeloRoute.Routing;

public sealed class RoutingResult<T>
{
    public T? Value { get; }
    public RoutingError? Error { get; }
    public bool IsSuccess => Error is null;

    private RoutingResult(T? value, RoutingError? error)
    {
        Value = value;
        Error = error;
    }

    public static RoutingResult<T> Success(T value) => new(value, null);
    public static RoutingResult<T> Failure(RoutingError error) => new(default, error);
}

public sealed record RoutingError(string Code, string Message);
