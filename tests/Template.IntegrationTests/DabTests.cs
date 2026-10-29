using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using Xunit;

namespace Template.IntegrationTests;

public partial class RuntimeTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DabRunsWithSeededSqlAndEnforcesConfiguredPermissions(bool simulatedIdentity)
    {
        await using var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.SmokeSql_AppHost>();
        var resource = Assert.IsAssignableFrom<ContainerResource>(builder.Resources.Single(r => r.Name == "dab-mcp"));
        var dab = builder.CreateResourceBuilder(resource)
            .WithEnvironment("APPLICATIONINSIGHTS_CONNECTION_STRING", "");
        var source = Path.Combine(Path.GetDirectoryName(Projects.SmokeSql_AppHost.ProjectPath)!, "dab-config.json");
        var testConfig = source + $".{Guid.NewGuid():N}.test.json";
        try
        {
            if (simulatedIdentity)
            {
                var config = JsonNode.Parse(await File.ReadAllTextAsync(source),
                    documentOptions: new() { CommentHandling = JsonCommentHandling.Skip })!;
                config["runtime"]!["host"]!["mode"] = "development";
                config["runtime"]!["host"]!["authentication"] = new JsonObject { ["provider"] = "Simulator" };
                await File.WriteAllTextAsync(testConfig, config.ToJsonString());
                dab.WithBindMount(testConfig, "/App/dab-config.json", isReadOnly: true);
            }
            await using var app = await StartAsync(builder);
            using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(5));
            await app.ResourceNotifications.WaitForResourceHealthyAsync("dab-mcp", cts.Token);
            using var http = app.CreateHttpClient("dab-mcp", "http");
            using var health = await http.GetAsync("/health", cts.Token);
            health.EnsureSuccessStatusCode();
            if (!simulatedIdentity)
            {
                using var anonymous = await http.GetAsync("/api/Product", cts.Token);
                Assert.Contains(anonymous.StatusCode, new[] { HttpStatusCode.Unauthorized, HttpStatusCode.Forbidden });
                return;
            }
            http.DefaultRequestHeaders.Add("X-MS-API-ROLE", "Developers");
            using var records = await http.GetAsync("/api/Product", cts.Token);
            records.EnsureSuccessStatusCode();
            using var json = JsonDocument.Parse(await records.Content.ReadAsStringAsync(cts.Token));
            Assert.NotEmpty(json.RootElement.GetProperty("value").EnumerateArray());
            await using var mcp = await McpClient.CreateAsync(new HttpClientTransport(
                new() { Endpoint = new Uri(http.BaseAddress!, "/mcp") }, http), cancellationToken: cts.Token);
            var tools = await mcp.ListToolsAsync(cancellationToken: cts.Token);
            Assert.Contains(tools, t => t.Name == "read_records");
            Assert.Contains(tools, t => t.Name == "create_record");
            Assert.DoesNotContain(tools, t => t.Name is "update_record" or "delete_record" or "execute_entity" or "aggregate_records");
            var entities = await mcp.CallToolAsync("describe_entities", cancellationToken: cts.Token);
            Assert.NotEqual(true, entities.IsError);
            Assert.Contains(entities.Content.OfType<TextContentBlock>(), c => c.Text.Contains("Product"));
            using var disallowedHost = new HttpRequestMessage(HttpMethod.Post, "/mcp") { Content = new StringContent("{}") };
            disallowedHost.Headers.Host = "untrusted.invalid";
            using var rejected = await http.SendAsync(disallowedHost, cts.Token);
            Assert.False(rejected.IsSuccessStatusCode);
        }
        finally
        {
            if (File.Exists(testConfig)) File.Delete(testConfig);
        }
    }
}
