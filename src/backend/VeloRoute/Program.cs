using System.Net;
using VeloRoute.Routing;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        var allowedOrigins = builder.Configuration["ALLOWED_ORIGINS"]?.Split(' ')
            ?? ["http://localhost:3000"];
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

builder.Services.Configure<OpenRouteServiceOptions>(
    builder.Configuration.GetSection("ORS"));

builder.Services.AddHttpClient<IOpenRouteServiceClient, OpenRouteServiceClient>()
    .ConfigureHttpClient((sp, client) =>
    {
        var opts = sp.GetRequiredService<IOptions<OpenRouteServiceOptions>>().Value;
        client.BaseAddress = new Uri(opts.BaseUrl);
        if (!string.IsNullOrEmpty(opts.ApiKey))
        {
            client.DefaultRequestHeaders.TryAddWithoutValidation("Authorization", opts.ApiKey);
        }
    })
    .AddStandardResilienceHandler(options =>
    {
        options.Retry.MaxRetryAttempts = 2;
        options.Retry.ShouldHandle = args => ValueTask.FromResult(
            args.Outcome.Result?.StatusCode is HttpStatusCode.RequestTimeout
                or >= HttpStatusCode.InternalServerError);
        options.CircuitBreaker.FailureRatio = 0.5;
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(10);
        options.CircuitBreaker.MinimumThroughput = 3;
        options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(30);
        options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(5);
    });

builder.Services.AddScoped<LoopRouteGenerator>();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwaggerUI(c => c.SwaggerEndpoint("/openapi/v1.json", "VeloRoute API v1"));
    app.UseHttpsRedirection();
}

app.UseCors();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
   .WithName("HealthCheck");

app.MapPost("/routes/loop", async (LoopRouteRequest req, LoopRouteGenerator gen, IOptions<OpenRouteServiceOptions> orsOpts, CancellationToken requestCt) =>
{
    if (req.MinKm < 5 || req.MaxKm > 300 || req.MinKm >= req.MaxKm)
        return Results.BadRequest(new { error = "Invalid distance range", code = "INVALID_INPUT" });

    if (req.StartLat < -90 || req.StartLat > 90 || req.StartLon < -180 || req.StartLon > 180)
        return Results.BadRequest(new { error = "Invalid coordinates", code = "INVALID_INPUT" });

    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(orsOpts.Value.TimeoutSeconds));
    using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(requestCt, timeoutCts.Token);

    try
    {
        var start = new RouteCoordinate(req.StartLon, req.StartLat);
        var result = await gen.GenerateAsync(start, req.MinKm, req.MaxKm, req.Seed, linkedCts.Token);

        if (!result.IsSuccess)
        {
            var (status, code) = result.Error!.Code switch
            {
                "2009" or "2010" => (422, "NO_ROUTE"),
                "2004"           => (429, "RATE_LIMITED"),
                "NO_VALID_RESULT" => (422, "NO_VALID_RESULT"),
                _                => (502, "PROVIDER_ERROR")
            };
            return Results.Json(new { error = result.Error.Message, code }, statusCode: status);
        }

        return Results.Ok(result.Value);
    }
    catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested)
    {
        return Results.Json(new { error = "Route generation timed out", code = "TIMEOUT" }, statusCode: 504);
    }
});

app.MapPost("/routes/gpx", (GpxRequest req) =>
{
    if (req.Coordinates is null || req.Coordinates.Count == 0)
        return Results.BadRequest(new { error = "Coordinates must not be empty", code = "INVALID_INPUT" });

    if (req.Coordinates.Any(c =>
            !double.IsFinite(c.Latitude)  || !double.IsFinite(c.Longitude) ||
            c.Latitude  < -90  || c.Latitude  > 90 ||
            c.Longitude < -180 || c.Longitude > 180))
        return Results.BadRequest(new { error = "One or more coordinates are out of range", code = "INVALID_INPUT" });

    var gpx = GpxSerializer.Serialize(req.Coordinates);
    return Results.Text(gpx, "application/gpx+xml");
});

app.Run();

record LoopRouteRequest(
    double StartLon, double StartLat,
    double MinKm,    double MaxKm,
    int?   Seed);

record GpxRequest(IReadOnlyList<RouteCoordinate> Coordinates);

public partial class Program { }
