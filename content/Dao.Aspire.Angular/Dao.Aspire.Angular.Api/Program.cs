#if (IncludeSignalR)
using Dao.Aspire.Angular.Api.Hubs;
using Microsoft.AspNetCore.SignalR;
#endif

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddOpenApi();

#if (IncludeSignalR)
builder.Services.AddSignalR();

// AllowCredentials requires explicit origins (not wildcard) for WebSocket connections
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins("http://localhost:4200", "https://localhost:4200")
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials();
    });
});
#endif

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();
#if (IncludeSignalR)
app.UseCors();
#endif
app.UseStaticFiles();

app.MapDefaultEndpoints();

var summaries = new[]
{
    "Freezing", "Bracing", "Chilly", "Cool", "Mild", "Warm", "Balmy", "Hot", "Sweltering", "Scorching"
};

app.MapGet("/api/weatherforecast", () =>
{
    var forecast = Enumerable.Range(1, 5).Select(index =>
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

#if (IncludeSignalR)
app.MapHub<NotificationHub>("/hubs/notifications");

// Test endpoint: POST /api/notify?message=hello  — broadcasts to all SignalR clients
app.MapPost("/api/notify", async (string message, IHubContext<NotificationHub, INotificationHubClient> hub) =>
{
    await hub.Clients.All.ReceiveNotification(message);
    return Results.Ok(new { sent = message });
})
.WithName("SendNotification");
#endif

app.MapFallbackToFile("index.html");

app.Run();

internal record WeatherForecast(DateOnly Date, int TemperatureC, string? Summary)
{
    public int TemperatureF => 32 + (int)(TemperatureC / 0.5556);
}
