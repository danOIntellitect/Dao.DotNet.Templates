extern alias AuthenticatedServer;

using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using Aspire.Hosting;
using Aspire.Hosting.Testing;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;
using AuthenticatedProgram = AuthenticatedServer::Program;

namespace Template.IntegrationTests;

public partial class RuntimeTests
{
    [Fact]
    public async Task McpChallengesAnonymousRequestsAndValidatesSignedTokens()
    {
        const string tenant = "11111111-1111-1111-1111-111111111111";
        const string audience = "22222222-2222-2222-2222-222222222222";
        const string issuer = $"https://login.microsoftonline.com/{tenant}/v2.0";
        using var rsa = RSA.Create(2048);
        var key = new RsaSecurityKey(rsa) { KeyId = "template-test" };
        await using var factory = new WebApplicationFactory<AuthenticatedProgram>().WithWebHostBuilder(web =>
        {
            web.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["AzureAd:Instance"] = "https://login.microsoftonline.com/",
                ["AzureAd:TenantId"] = tenant,
                ["AzureAd:ClientId"] = audience,
                ["AzureAd:Audience"] = audience,
                ["AzureAd:Scopes:0"] = $"api://{audience}/access_as_user"
            }));
            web.ConfigureTestServices(services => services.PostConfigure<JwtBearerOptions>(
                JwtBearerDefaults.AuthenticationScheme, options =>
                {
                    options.Authority = null;
                    options.MetadataAddress = null;
                    options.ConfigurationManager = null;
                    options.TokenValidationParameters = new()
                    {
                        ValidateAudience = true,
                        ValidAudience = audience,
                        ValidateIssuer = true,
                        ValidIssuer = issuer,
                        ValidateLifetime = true,
                        ValidateIssuerSigningKey = true,
                        IssuerSigningKey = key,
                        ClockSkew = TimeSpan.FromMinutes(5),
                    };
                }));
        });
        using var http = factory.CreateClient(new() { BaseAddress = new Uri("https://localhost") });
        using var anonymous = await http.GetAsync("/mcp");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.NotEmpty(anonymous.Headers.WwwAuthenticate);

        var token = new JwtSecurityToken(issuer, audience,
            [new Claim("sub", "regression-user"), new Claim("name", "Regression User"), new Claim("scp", "access_as_user")],
            DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(5),
            new SigningCredentials(key, SecurityAlgorithms.RsaSha256));
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", new JwtSecurityTokenHandler().WriteToken(token));
        await using var mcp = await McpClient.CreateAsync(new HttpClientTransport(
            new() { Endpoint = new Uri(http.BaseAddress!, "/mcp") }, http));
        var tools = await mcp.ListToolsAsync();
        var userTool = Assert.Single(tools.Where(t => t.Name.Contains("user", StringComparison.OrdinalIgnoreCase)));
        var result = await mcp.CallToolAsync(userTool.Name);
        Assert.NotEqual(true, result.IsError);
        Assert.Contains(result.Content.OfType<TextContentBlock>(), c => c.Text.Contains("Regression User"));
    }
}
