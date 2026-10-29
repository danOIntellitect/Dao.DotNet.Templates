using Dao.Aspire.Mcp.Server.Tools;
using Dao.Aspire.Mcp.Tests.Fixtures;

namespace Dao.Aspire.Mcp.Tests;

public class ToolTests
{
    [Theory]
    [InlineData("")]
    [InlineData("a message with \"quotes\" and a newline\n")]
    public void EchoPreservesInput(string message)
    {
        Assert.Equal($"Hello from MCP Server. Your input: {message}", BasicTools.Echo(message));
    }

    [Fact]
    public void HealthReportsOperationalState()
    {
        Assert.StartsWith("MCP server is operational. Checked: ", BasicTools.AnalyzeMCPServerHealth());
    }

#if IncludeAuthentication
    [Fact]
    public void UserToolReportsAuthenticatedClaims()
    {
        var tool = new AuthenticatedTool(MockServiceFixtures.CreateMockHttpContextAccessor(
            userName: "Regression User", roles: ["Admin", "DataAnalyst"]));
        var result = tool.AnalyzeMCPServerUser();
        Assert.Contains("Regression User", result);
        Assert.Contains("Admin, DataAnalyst", result);
    }

    [Fact]
    public void UserToolDoesNotInventAnIdentity()
    {
        var tool = new AuthenticatedTool(MockServiceFixtures.CreateUnauthenticatedHttpContextAccessor());
        Assert.Contains("no authenticated user found", tool.AnalyzeMCPServerUser());
    }
#endif
}
