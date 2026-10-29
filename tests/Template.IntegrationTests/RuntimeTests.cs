using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Aspire.Hosting.JavaScript;
using Microsoft.Extensions.Configuration;
using Microsoft.Playwright;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Template.IntegrationTests;

public partial class RuntimeTests
{
    [Theory(Timeout = 300_000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AngularRendersRealApiDataAndSignalR(bool signalR)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        await using var builder = signalR
            ? await DistributedApplicationTestingBuilder.CreateAsync<Projects.SmokeSignalR_AppHost>(cancellationToken: cts.Token)
            : await DistributedApplicationTestingBuilder.CreateAsync<Projects.SmokeAngular_AppHost>(cancellationToken: cts.Token);
        await using var app = await StartAsync(builder);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("api", cts.Token);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("frontend", cts.Token);
        using var api = app.CreateHttpClient("api");
        await AssertForecastsAsync(api, cts.Token);

        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        var page = await browser.NewPageAsync(new() { IgnoreHTTPSErrors = true });
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        page.WebSocket += (_, socket) => socket.FrameReceived += (_, frame) =>
        {
            if (frame.Text == "{}\u001e") connected.TrySetResult();
        };
        var forecastResponse = page.WaitForResponseAsync(r => r.Url.Contains("/api/weatherforecast") && r.Status == 200);
        await page.GotoAsync(app.GetEndpoint("frontend").ToString());
        var response = await forecastResponse;
        using var data = JsonDocument.Parse(await response.TextAsync());
        await Assertions.Expect(page.Locator("tbody tr")).ToHaveCountAsync(5);
        await Assertions.Expect(page.Locator("tbody")).ToContainTextAsync(
            data.RootElement[0].GetProperty("temperatureC").GetInt32().ToString());
        if (signalR)
        {
            await connected.Task.WaitAsync(cts.Token);
            using var notification = await api.PostAsync("/api/notify?message=template-regression", null, cts.Token);
            notification.EnsureSuccessStatusCode();
            await Assertions.Expect(page.Locator("app-notification-badge")).ToContainTextAsync("template-regression");
        }
        else
        {
            await Assertions.Expect(page.Locator("app-notification-badge")).ToHaveCountAsync(0);
            using var missingHub = await api.PostAsync("/hubs/notifications/negotiate?negotiateVersion=1", null, cts.Token);
            Assert.False(missingHub.IsSuccessStatusCode);
        }
        await page.RouteAsync("**/api/weatherforecast", route => route.FulfillAsync(new() { Status = 503, Body = "Unavailable" }));
        await page.ReloadAsync();
        await Assertions.Expect(page.Locator(".weather .error")).ToContainTextAsync("Failed to load weather data");
        Assert.Empty(errors);
    }

    [Fact(Timeout = 300_000)]
    public async Task EfMigratesAndReturnsSeededTodos()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.SmokeEf_AppHost>(cancellationToken: cts.Token);
        await using var app = await StartAsync(builder);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("api-service", cts.Token);
        using var api = app.CreateHttpClient("api-service");
        using var result = await api.GetFromJsonAsync<JsonDocument>("/todos", cts.Token);
        Assert.NotNull(result);
        Assert.Single(result.RootElement.EnumerateArray());
        Assert.Equal("Initial Todo", result.RootElement[0].GetProperty("name").GetString());
    }

    [Fact(Timeout = 300_000)]
    public async Task ExpoWebRendersRealApiData()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.SmokeExpo_AppHost>(cancellationToken: cts.Token);
        var mobile = builder.CreateResourceBuilder(
            Assert.IsType<JavaScriptAppResource>(builder.Resources.Single(r => r.Name == "mobile")));
        mobile.WithRunScript("web").WithHttpEndpoint(targetPort: 8081, env: "PORT", name: "http");
        await using var app = await StartAsync(builder);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("api", cts.Token);
        await app.ResourceNotifications.WaitForResourceAsync("mobile", KnownResourceStates.Running, cts.Token);
        using var api = app.CreateHttpClient("api");
        await AssertForecastsAsync(api, cts.Token);
        using var playwright = await Playwright.CreateAsync();
        await using var browser = await playwright.Chromium.LaunchAsync(new() { Headless = true });
        var page = await browser.NewPageAsync(new() { IgnoreHTTPSErrors = true });
        var errors = new List<string>();
        page.PageError += (_, error) => errors.Add(error);
        await page.GotoAsync(app.GetEndpoint("mobile", "http").ToString(), new() { Timeout = 120_000 });
        await Assertions.Expect(page.GetByText("Weather Forecast", new() { Exact = true })).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByText("Live data from the C# API")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByText(DateTime.UtcNow.AddDays(1).ToString("yyyy-MM-dd"), new() { Exact = true })).ToBeVisibleAsync();
        Assert.Empty(errors);
    }

    [Fact(Timeout = 300_000)]
    public async Task McpInvokesToolsWithoutCloudCredentials()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(3));
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.SmokeMcp2_AppHost>(cancellationToken: cts.Token);
        await using var app = await StartAsync(builder);
        await app.ResourceNotifications.WaitForResourceAsync("mcp-server", KnownResourceStates.Running, cts.Token);
        using var http = app.CreateHttpClient("mcp-server");
        await using var client = await McpClient.CreateAsync(new HttpClientTransport(
            new() { Endpoint = new Uri(http.BaseAddress!, "/mcp") }, http), cancellationToken: cts.Token);
        var tools = await client.ListToolsAsync(cancellationToken: cts.Token);
        Assert.Contains(tools, t => t.Name == "echo");
        var result = await client.CallToolAsync("echo", new Dictionary<string, object?> { ["message"] = "regression" },
            cancellationToken: cts.Token);
        Assert.NotEqual(true, result.IsError);
        Assert.Contains(result.Content.OfType<TextContentBlock>(), c => c.Text.Contains("regression"));
        Assert.DoesNotContain(tools, t => t.Name.Contains("inventory", StringComparison.OrdinalIgnoreCase));
    }

    internal static async Task<DistributedApplication> StartAsync(IDistributedApplicationTestingBuilder builder)
    {
        builder.Configuration["Azure:Provisioning:Enabled"] = "false";
        foreach (var resource in builder.Resources.ToArray())
        {
            if (resource.Name is "inspector" or "MyApplicationInsights")
            {
                builder.Resources.Remove(resource);
                continue;
            }
            if (resource is ContainerResource container)
            {
                builder.CreateResourceBuilder(container).WithLifetime(ContainerLifetime.Session);
                if (container.Name is "sql-server" or "postgres")
                    foreach (var mount in container.Annotations.OfType<ContainerMountAnnotation>().ToArray())
                        container.Annotations.Remove(mount);
            }
            if (resource is ProjectResource project)
            {
                builder.CreateResourceBuilder(project)
                    .WithEnvironment("AzureOpenAI__Endpoint", "https://example.invalid/")
                    .WithEnvironment("AzureOpenAI__DeploymentName", "test-only")
                    .WithEnvironment("AzureAd__Instance", "https://login.microsoftonline.com/")
                    .WithEnvironment("AzureAd__TenantId", "11111111-1111-1111-1111-111111111111")
                    .WithEnvironment("AzureAd__ClientId", "22222222-2222-2222-2222-222222222222")
                    .WithEnvironment("AzureAd__Audience", "22222222-2222-2222-2222-222222222222");
            }
        }
        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        var app = await builder.BuildAsync(cts.Token);
        try
        {
            await app.StartAsync(cts.Token);
            return app;
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }
    }

    private static async Task AssertForecastsAsync(HttpClient client, CancellationToken cancellationToken)
    {
        using var result = await client.GetFromJsonAsync<JsonDocument>("/api/weatherforecast", cancellationToken);
        Assert.NotNull(result);
        Assert.Equal(5, result.RootElement.GetArrayLength());
        foreach (var forecast in result.RootElement.EnumerateArray())
        {
            Assert.True(DateOnly.TryParse(forecast.GetProperty("date").GetString(), out _));
            Assert.InRange(forecast.GetProperty("temperatureC").GetInt32(), -20, 54);
            Assert.Equal(32 + (int)(forecast.GetProperty("temperatureC").GetInt32() / 0.5556),
                forecast.GetProperty("temperatureF").GetInt32());
        }
    }
}
