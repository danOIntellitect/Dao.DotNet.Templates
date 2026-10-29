using System.Net;
using System.Net.Http.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using SmokeAvalonia.Desktop;
using SmokeAvalonia.Desktop.Infrastructure;
using SmokeAvalonia.Desktop.Services;
using SmokeAvalonia.Desktop.ViewModels;
using SmokeAvalonia.Desktop.Views;
using SmokeAvalonia.Shared.Models;
using Xunit;

[assembly: AvaloniaTestApplication(typeof(Template.DesktopTests.TestApplication))]

namespace Template.DesktopTests;

public static class TestApplication
{
    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>()
        .UseHeadless(new AvaloniaHeadlessPlatformOptions()).WithInterFont();
}

public class WeatherTests
{
    [AvaloniaFact]
    public void WeatherViewLoadsThemeBindingsAndApiRecords()
    {
        var viewModel = new WeatherViewModel(new TestApi());
        var view = new WeatherView { DataContext = viewModel };
        var window = new Window { Content = view };
        window.Show();
        try
        {
            Assert.False(viewModel.IsLoading);
            Assert.Null(viewModel.ErrorMessage);
            var grid = Assert.Single(view.GetVisualDescendants().OfType<DataGrid>());
            Assert.Single(grid.ItemsSource.Cast<WeatherForecast>());
            Assert.Equal(4, grid.Columns.Count);
            Assert.NotNull(grid.Template);
        }
        finally
        {
            window.Close();
        }
    }

    [Fact]
    public async Task WeatherViewModelReportsApiFailure()
    {
        var viewModel = new WeatherViewModel(new TestApi(fail: true));
        await viewModel.LoadAsync();
        Assert.False(viewModel.IsLoading);
        Assert.Contains("Unavailable", viewModel.ErrorMessage);
        Assert.Empty(viewModel.Forecasts);
    }

    [Fact]
    public async Task ApiServiceUsesConfiguredEndpointAndPropagatesFailures()
    {
        using var http = new HttpClient(new ApiHandler()) { BaseAddress = new Uri("http://localhost:5123/") };
        var service = new ApiService(http);
        var forecasts = await service.GetWeatherForecastAsync();
        Assert.Single(forecasts);
        Assert.Equal("Mild", forecasts[0].Summary);
        using var failingHttp = new HttpClient(new ApiHandler(fail: true)) { BaseAddress = http.BaseAddress };
        await Assert.ThrowsAsync<HttpRequestException>(() => new ApiService(failingHttp).GetWeatherForecastAsync());
    }

    private sealed class TestApi(bool fail = false) : IApiService
    {
        public Task<List<WeatherForecast>> GetWeatherForecastAsync(CancellationToken ct = default) =>
            fail ? Task.FromException<List<WeatherForecast>>(new HttpRequestException("Unavailable"))
            : Task.FromResult<List<WeatherForecast>>([new(new DateOnly(2026, 1, 1), 20, 67, "Mild")]);
    }

    private sealed class ApiHandler(bool fail = false) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal("http://localhost:5123/api/weatherforecast", request.RequestUri!.ToString());
            return Task.FromResult(fail ? new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new[] { new WeatherForecast(new DateOnly(2026, 1, 1), 20, 67, "Mild") })
                });
        }
    }
}
