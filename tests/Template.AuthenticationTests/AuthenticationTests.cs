extern alias CookieServer;
extern alias JwtServer;
extern alias MsalServer;

using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.TestHost;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Server;
using Xunit;
using CookieProgram = CookieServer::Program;
using JwtProgram = JwtServer::Program;
using MsalProgram = MsalServer::Program;

namespace Template.AuthenticationTests;

public sealed class AuthenticationTests
{
    private const string Origin = "https://localhost:4200";
    private const string Tenant = "11111111-1111-1111-1111-111111111111";
    private const string Audience = "22222222-2222-2222-2222-222222222222";
    private const string Issuer = $"https://login.microsoftonline.com/{Tenant}/v2.0";

    [Theory]
    [InlineData("Authentication:Google:ClientId")]
    [InlineData("Authentication:Google:ClientSecret")]
    public void GoogleVariantsFailStartupWithoutRequiredSettings(string key)
    {
        AssertMissingConfiguration<CookieProgram>(key, "Authentication:Google:ClientId and ClientSecret");
        AssertMissingConfiguration<JwtProgram>(key, "Authentication:Google:ClientId and ClientSecret");
    }

    [Theory]
    [InlineData("AzureAd:TenantId")]
    [InlineData("AzureAd:ClientId")]
    [InlineData("Authentication:SpaClientId")]
    public void MsalFailsStartupWithoutRequiredSettings(string key) =>
        AssertMissingConfiguration<MsalProgram>(key, "AzureAd:TenantId, AzureAd:ClientId (API), and Authentication:SpaClientId");

    [Theory]
    [InlineData("http://localhost:4200")]
    [InlineData("https://localhost:4200/not-an-origin")]
    [InlineData("https://localhost:4200?query=value")]
    [InlineData("https://localhost:4200#fragment")]
    [InlineData("https://user:password@localhost:4200")]
    public void AuthenticationRejectsInvalidPublicOrigins(string origin)
    {
        using var factory = new AuthFactory<CookieProgram>(new() { ["Authentication:PublicOrigin"] = origin });
        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
        Assert.Contains("public HTTPS origin", exception.Message);
    }

    [Fact]
    public async Task CookieAuthCompletesGoogleLoginProtectsMutationsAndLogsOut()
    {
        await using var factory = CreateFactory<CookieProgram>();
        using var http = Client(factory);
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/api/weatherforecast", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.PostAsync("/api/notify?message=test", null, TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.PostAsync("/hubs/notifications/negotiate?negotiateVersion=1", null, TestContext.Current.CancellationToken)).StatusCode);
        await GoogleLogin(http, "/api/auth/login");
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/api/auth/me", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/api/weatherforecast", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await http.PostAsync("/api/notify?message=test", null, TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await http.PostAsync("/api/auth/logout", null, TestContext.Current.CancellationToken)).StatusCode);
        var csrf = await SetCsrfHeader(http);
        Assert.False(string.IsNullOrEmpty(csrf));
        Assert.Equal(HttpStatusCode.OK, (await http.PostAsync("/api/notify?message=test", null, TestContext.Current.CancellationToken)).StatusCode);
        await AssertSignalR(factory, http, null, csrf);
        Assert.Equal(HttpStatusCode.NoContent, (await http.PostAsync("/api/auth/logout", null, TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/api/weatherforecast", TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task GoogleRejectsMissingOrTamperedOAuthState()
    {
        await using var factory = CreateFactory<CookieProgram>();
        using var http = Client(factory);
        var response = await http.GetAsync("/signin-google?code=fake&state=tampered", TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("authError=google", response.Headers.Location!.ToString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/api/auth/me", TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task CookieAuthRejectsCrossOriginWebSocketUpgrades()
    {
        await using var factory = CreateFactory<CookieProgram>();
        using var http = Client(factory);
        var cookies = new CookieContainer();
        await GoogleLogin(http, "/api/auth/login", cookies);
        var socketClient = factory.Server.CreateWebSocketClient();
        socketClient.ConfigureRequest = request =>
        {
            request.Headers.Cookie = cookies.GetCookieHeader(new Uri(Origin));
            request.Headers.Origin = "https://attacker.invalid";
        };
        var rejected = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await socketClient.ConnectAsync(new Uri("wss://localhost:4200/hubs/notifications"),
                TestContext.Current.CancellationToken));
        Assert.Contains("403", rejected.Message);
        socketClient.ConfigureRequest = request =>
        {
            request.Headers.Cookie = cookies.GetCookieHeader(new Uri(Origin));
            request.Headers.Origin = Origin;
        };
        using var socket = await socketClient.ConnectAsync(new Uri("wss://localhost:4200/hubs/notifications"),
            TestContext.Current.CancellationToken);
        Assert.Equal(WebSocketState.Open, socket.State);
    }

    [Fact]
    public async Task JwtAuthRequiresPkceAndRefreshesRevokesAndProtectsSignalR()
    {
        await using var factory = CreateFactory<JwtProgram>();
        using var http = Client(factory);
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/api/weatherforecast", TestContext.Current.CancellationToken)).StatusCode);
        var verifier = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        var authorize = AuthorizationUrl(verifier);
        var code = await GoogleLogin(http, authorize);
        var tokens = await Exchange(http, code!, verifier);
        var access = tokens.GetProperty("access_token").GetString()!;
        var refresh = tokens.GetProperty("refresh_token").GetString()!;
        Assert.Equal(3, access.Split('.').Length);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", access);
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/api/weatherforecast", TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/api/auth/me", TestContext.Current.CancellationToken)).StatusCode);
        await AssertSignalR(factory, http, access, null);
        http.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await http.GetAsync("/api/weatherforecast?access_token=" + access, TestContext.Current.CancellationToken)).StatusCode);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", access);

        using var renewed = await http.PostAsync("/connect/token", Form(new()
        {
            ["grant_type"] = "refresh_token", ["client_id"] = "angular", ["refresh_token"] = refresh
        }), TestContext.Current.CancellationToken);
        renewed.EnsureSuccessStatusCode();
        using var renewedData = JsonDocument.Parse(await renewed.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var newAccess = renewedData.RootElement.GetProperty("access_token").GetString()!;
        var newRefresh = renewedData.RootElement.GetProperty("refresh_token").GetString()!;
        Assert.NotEqual(refresh, newRefresh);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", newAccess);
        Assert.Equal(HttpStatusCode.OK, (await http.GetAsync("/api/weatherforecast", TestContext.Current.CancellationToken)).StatusCode);
        http.DefaultRequestHeaders.Authorization = null;
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.PostAsync("/api/auth/logout", null, TestContext.Current.CancellationToken)).StatusCode);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", newAccess);
        Assert.Equal(HttpStatusCode.NoContent, (await http.PostAsync("/api/auth/logout", null, TestContext.Current.CancellationToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/api/weatherforecast", TestContext.Current.CancellationToken)).StatusCode);
        using var revoked = await http.PostAsync("/connect/token", Form(new()
        {
            ["grant_type"] = "refresh_token", ["client_id"] = "angular", ["refresh_token"] = newRefresh
        }), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, revoked.StatusCode);
    }

    [Fact]
    public async Task JwtAuthRejectsRedirectManipulationMissingPkceAndRefreshReuse()
    {
        await using var factory = CreateFactory<JwtProgram>();
        using var http = Client(factory);
        var verifier = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        var badRedirect = await http.GetAsync(AuthorizationUrl(verifier).Replace(
            Uri.EscapeDataString(Origin + "/auth/callback"), Uri.EscapeDataString("https://attacker.invalid/callback")), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, badRedirect.StatusCode);
        var missingPkce = await http.GetAsync("/connect/authorize?client_id=angular&response_type=code&redirect_uri=" +
            Uri.EscapeDataString(Origin + "/auth/callback") + "&scope=openid%20api", TestContext.Current.CancellationToken);
        Assert.NotEqual("accounts.google.com", missingPkce.Headers.Location?.Host);
        var code = await GoogleLogin(http, AuthorizationUrl(verifier));
        var tokens = await Exchange(http, code!, verifier);
        var refresh = tokens.GetProperty("refresh_token").GetString()!;
        using var renewed = await http.PostAsync("/connect/token", Form(new()
        {
            ["grant_type"] = "refresh_token", ["client_id"] = "angular", ["refresh_token"] = refresh
        }), TestContext.Current.CancellationToken);
        renewed.EnsureSuccessStatusCode();
        using var refreshed = JsonDocument.Parse(await renewed.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        var access = refreshed.RootElement.GetProperty("access_token").GetString()!;
        using var replayed = await http.PostAsync("/connect/token", Form(new()
        {
            ["grant_type"] = "refresh_token", ["client_id"] = "angular", ["refresh_token"] = refresh
        }), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, replayed.StatusCode);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", access);
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/api/weatherforecast", TestContext.Current.CancellationToken)).StatusCode);
    }

    [Fact]
    public async Task JwtAuthRejectsIncorrectPkceVerifier()
    {
        await using var factory = CreateFactory<JwtProgram>();
        using var http = Client(factory);
        var verifier = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));
        var code = await GoogleLogin(http, AuthorizationUrl(verifier));
        using var incorrect = await http.PostAsync("/connect/token", Form(new()
        {
            ["grant_type"] = "authorization_code", ["client_id"] = "angular", ["code"] = code!,
            ["redirect_uri"] = Origin + "/auth/callback",
            ["code_verifier"] = Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32)),
        }), TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, incorrect.StatusCode);
    }

    [Theory]
    [InlineData("api", Issuer, false, "access_as_user", 200)]
    [InlineData("wrong-audience", Issuer, false, "access_as_user", 401)]
    [InlineData(Audience, "https://attacker.invalid/", false, "access_as_user", 401)]
    [InlineData(Audience, Issuer, true, "access_as_user", 401)]
    [InlineData(Audience, Issuer, false, "wrong-scope", 403)]
    public async Task MsalValidatesAudienceIssuerLifetimeAndScope(string audience, string issuer,
        bool expired, string scope, int expected)
    {
        using var rsa = RSA.Create(2048);
        var key = new RsaSecurityKey(rsa) { KeyId = "test-key" };
        await using var factory = CreateFactory<MsalProgram>().WithWebHostBuilder(web =>
            web.ConfigureTestServices(services => services.PostConfigure<JwtBearerOptions>(
                JwtBearerDefaults.AuthenticationScheme, options =>
                {
                    options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(
                        new OpenIdConnectConfiguration { Issuer = Issuer, SigningKeys = { key } });
                    options.TokenValidationParameters = new()
                    {
                        ValidateIssuer = true, ValidIssuer = Issuer,
                        ValidateAudience = true, ValidAudience = Audience,
                        ValidateLifetime = true, ValidateIssuerSigningKey = true,
                        IssuerSigningKey = key, ClockSkew = TimeSpan.Zero,
                    };
                })));
        using var http = Client(factory);
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/api/weatherforecast", TestContext.Current.CancellationToken)).StatusCode);
        var token = new JwtSecurityToken(issuer, audience == "api" ? Audience : audience,
            [new Claim("sub", "test-user"), new Claim("name", "Test User"), new Claim("scp", scope),
                new Claim("tid", Tenant), new Claim("ver", "2.0")],
            DateTime.UtcNow.AddMinutes(-20), DateTime.UtcNow.AddMinutes(expired ? -10 : 10),
            new SigningCredentials(key, SecurityAlgorithms.RsaSha256));
        var access = new JwtSecurityTokenHandler().WriteToken(token);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", access);
        using var forecast = await http.GetAsync("/api/weatherforecast", TestContext.Current.CancellationToken);
        Assert.True(forecast.StatusCode == (HttpStatusCode)expected,
            $"Expected {expected}, got {forecast.StatusCode}: {forecast.Headers.WwwAuthenticate}");
        if (expected == 200) await AssertSignalR(factory, http, access, null);
    }

    private static WebApplicationFactory<T> CreateFactory<T>() where T : class => new AuthFactory<T>();

    private static void AssertMissingConfiguration<T>(string key, string expectedMessage) where T : class
    {
        using var factory = new AuthFactory<T>(new() { [key] = string.Empty });
        var exception = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
        Assert.Contains(expectedMessage, exception.Message);
    }

    private sealed class AuthFactory<T>(Dictionary<string, string?>? overrides = null) : WebApplicationFactory<T> where T : class
    {
        private readonly string database = Path.Combine(Path.GetTempPath(), $"dao-auth-test-{Guid.NewGuid():N}.db");

        protected override IHost CreateHost(IHostBuilder builder)
        {
            builder.ConfigureHostConfiguration(configuration =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                ["Authentication:PublicOrigin"] = Origin,
                ["Authentication:Google:ClientId"] = "test-client",
                ["Authentication:Google:ClientSecret"] = "test-only",
                ["Authentication:SpaClientId"] = "33333333-3333-3333-3333-333333333333",
                ["AzureAd:TenantId"] = Tenant,
                ["AzureAd:ClientId"] = Audience,
                ["ConnectionStrings:Authentication"] = $"Data Source={database};Pooling=False",
                ["Logging:LogLevel:Default"] = "Warning",
                });
                if (overrides is not null) configuration.AddInMemoryCollection(overrides);
            });
            return base.CreateHost(builder);
        }

        protected override void ConfigureWebHost(IWebHostBuilder web)
        {
            web.UseEnvironment("Development");
            // IdentityModel can retain the first host's logger globally. It must remain safe after disposal.
            web.ConfigureLogging(logging => logging.ClearProviders().AddProvider(NullLoggerProvider.Instance));
            web.ConfigureTestServices(services => services.Configure<GoogleOptions>(
                "Google", options => options.Backchannel = new HttpClient(new FakeGoogleHandler())));
            if (typeof(T) == typeof(JwtProgram))
            {
                web.ConfigureTestServices(services =>
                {
                    // Each in-process test issuer owns independent key material.
                    services.Configure<OpenIddictServerOptions>(options =>
                    {
                        options.SigningCredentials.Clear();
                        options.EncryptionCredentials.Clear();
                    });
                    services.AddOpenIddict().AddServer(options =>
                        options.AddEphemeralEncryptionKey().AddEphemeralSigningKey());
                });
            }

        }

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            if (File.Exists(database)) File.Delete(database);
            if (File.Exists(database + "-shm")) File.Delete(database + "-shm");
            if (File.Exists(database + "-wal")) File.Delete(database + "-wal");
        }
    }

    private static HttpClient Client<T>(WebApplicationFactory<T> factory) where T : class =>
        factory.CreateClient(new() { BaseAddress = new Uri(Origin), AllowAutoRedirect = false });

    private static string AuthorizationUrl(string verifier) => QueryHelpers.AddQueryString("/connect/authorize",
        new Dictionary<string, string?>
        {
            ["client_id"] = "angular", ["response_type"] = "code", ["redirect_uri"] = Origin + "/auth/callback",
            ["scope"] = "openid profile offline_access api", ["state"] = "test-state", ["nonce"] = "test-nonce",
            ["code_challenge"] = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(verifier))),
            ["code_challenge_method"] = "S256",
        });

    private static async Task<string?> GoogleLogin(HttpClient http, string start, CookieContainer? cookies = null)
    {
        using var challenge = await http.GetAsync(start);
        Assert.Equal(HttpStatusCode.Redirect, challenge.StatusCode);
        var destination = challenge.Headers.Location!;
        Assert.Equal("accounts.google.com", destination.Host);
        var state = QueryHelpers.ParseQuery(destination.Query)["state"].ToString();
        var googleCallback = QueryHelpers.AddQueryString("/signin-google", new Dictionary<string, string?>
        {
            ["state"] = state, ["code"] = "fake-google-code"
        });
        using var callback = await http.GetAsync(googleCallback);
        if (cookies is not null && callback.Headers.TryGetValues("Set-Cookie", out var cookieHeaders))
            foreach (var header in cookieHeaders) cookies.SetCookies(new Uri(Origin), header);
        Assert.Equal(HttpStatusCode.Redirect, callback.StatusCode);
        Assert.DoesNotContain("authError", callback.Headers.Location!.ToString());
        var location = callback.Headers.Location.ToString();
        for (var index = 0; index < 3 && location.StartsWith("/"); index++)
        {
            if (location == "/") return null;
            using var next = await http.GetAsync(location);
            Assert.Equal(HttpStatusCode.Redirect, next.StatusCode);
            location = next.Headers.Location!.ToString();
        }
        return QueryHelpers.ParseQuery(new Uri(location).Query)["code"].ToString();
    }

    private static async Task<JsonElement> Exchange(HttpClient http, string code, string verifier)
    {
        using var response = await http.PostAsync("/connect/token", Form(new()
        {
            ["grant_type"] = "authorization_code", ["client_id"] = "angular", ["code"] = code,
            ["redirect_uri"] = Origin + "/auth/callback", ["code_verifier"] = verifier
        }));
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.IsSuccessStatusCode, body);
        using var document = JsonDocument.Parse(body);
        return document.RootElement.Clone();
    }

    private static FormUrlEncodedContent Form(Dictionary<string, string> values) => new(values);

    private static async Task<string> SetCsrfHeader(HttpClient http)
    {
        var data = await http.GetFromJsonAsync<JsonElement>("/api/auth/csrf");
        var token = data.GetProperty("token").GetString()!;
        http.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");
        http.DefaultRequestHeaders.Add("X-XSRF-TOKEN", token);
        return token;
    }

    private static async Task AssertSignalR<T>(WebApplicationFactory<T> factory, HttpClient http,
        string? token, string? csrf) where T : class
    {
        await using var hub = new HubConnectionBuilder().WithUrl(Origin + "/hubs/notifications", options =>
        {
            options.Transports = HttpTransportType.LongPolling;
            options.HttpMessageHandlerFactory = _ => factory.Server.CreateHandler();
            if (token is not null) options.AccessTokenProvider = () => Task.FromResult<string?>(token);
            if (csrf is not null)
            {
                options.Headers["X-XSRF-TOKEN"] = csrf;
                // Forward the real authenticated session issued by the cookie middleware.
                options.HttpMessageHandlerFactory = _ => new CookieForwardingHandler(http, factory.Server.CreateHandler());
            }
        }).Build();
        var received = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        hub.On<string>("ReceiveNotification", value => received.TrySetResult(value));
        await hub.StartAsync();
        using var response = await http.PostAsync("/api/notify?message=authenticated-test", null);
        response.EnsureSuccessStatusCode();
        Assert.Equal("authenticated-test", await received.Task.WaitAsync(TimeSpan.FromSeconds(10)));
        await hub.StopAsync();
    }

    private sealed class CookieForwardingHandler(HttpClient client, HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // Use the factory client's cookie container for same-origin SignalR traffic.
            var copy = new HttpRequestMessage(request.Method, request.RequestUri) { Content = request.Content };
            foreach (var header in request.Headers) copy.Headers.TryAddWithoutValidation(header.Key, header.Value);
            return await client.SendAsync(copy, cancellationToken);
        }
    }

    private sealed class FakeGoogleHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var json = request.RequestUri!.AbsolutePath.Contains("token")
                ? """{"access_token":"fake-google-access","token_type":"Bearer","expires_in":3600}"""
                : """{"id":"stable-google-subject","name":"Google Test User","email":"test@example.invalid","verified_email":true}""";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json")
            });
        }
    }
}
