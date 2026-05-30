using bootstrap_scaffold.Routing;
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
        options.CircuitBreaker.FailureRatio = 0.5;
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(10);
        options.CircuitBreaker.MinimumThroughput = 3;
        options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(30);
        options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(5);
    });

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseHttpsRedirection();

    app.MapGet("/routes/preview", async (IOpenRouteServiceClient client, CancellationToken ct) =>
    {
        var start = new RouteCoordinate(16.3725, 48.2085); // Vienna
        var end   = new RouteCoordinate(16.3900, 48.2200);
        var result = await client.GetDirectionsAsync(start, end, ct);
        return result.IsSuccess
            ? Results.Ok(result.Value)
            : Results.Problem(result.Error!.Message, statusCode: 502);
    });
}

app.UseCors();

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
   .WithName("HealthCheck");

app.MapGet("/weatherforecast", () =>
{
    var forecast =  Enumerable.Range(1, 5).Select(index =>
        new WeatherForecast
        (
            DateOnly.FromDateTime(DateTime.Now.AddDays(index)),
            Random.Shared.Next(-20, 55),
            summaries[Random.Shared.Next(summaries.Length)]
        ))
        .ToArray();
    return forecast;
})
.WithName("GetWeatherForecast");

app.Run();

record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
