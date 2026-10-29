using Dao.Aspire.Mcp.Shared;
#if IncludeDatabase
using Dao.Aspire.Mcp.Data;
#endif
#if IncludeAuthentication
using Dao.Aspire.Mcp.Shared.Options;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Identity.Web;
using ModelContextProtocol.AspNetCore.Authentication;
#endif

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

#if IncludeDatabase
builder.AddSqlServerDbContext<AppDbContext>(ProjectNames.Database);
#endif

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
    });
});

#if IncludeAuthentication
builder
    .Services.AddOptions<AzureAdOptions>()
    .BindConfiguration(AzureAdOptions.SectionName)
    .ValidateDataAnnotations()
    .ValidateOnStart();

var azureAdOptions =
    builder.Configuration.GetSection(AzureAdOptions.SectionName).Get<AzureAdOptions>()
    ?? throw new InvalidOperationException("Azure AD options are required");

var authBuilder = builder
    .Services.AddAuthentication(options =>
    {
        options.DefaultChallengeScheme = McpAuthenticationDefaults.AuthenticationScheme;
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddMcp(options =>
    {
        options.ResourceMetadata = new()
        {
            AuthorizationServers =
            {
                // Configure the authorization server based on Entra ID (Azure AD) v2.0 endpoint for OAuth 2.1
                new($"{azureAdOptions.Instance}{azureAdOptions.TenantId}/v2.0"),
            },
            ScopesSupported = [.. azureAdOptions.Scopes],
        };
    })
    .AddMicrosoftIdentityWebApi(
        jwtOptions =>
        {
            jwtOptions.TokenValidationParameters = new()
            {
                ValidateAudience = true,
                ValidAudience =
                    azureAdOptions.Audience
                    ?? throw new InvalidOperationException("Audience is required"),
                ValidateIssuer = true,
                ValidIssuer = $"{azureAdOptions.Instance.TrimEnd('/')}/{azureAdOptions.TenantId}/v2.0",
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ClockSkew = TimeSpan.FromMinutes(5),
            };
        },
        microsoftOptions =>
        {
            microsoftOptions.Instance = azureAdOptions.Instance;
            microsoftOptions.ClientId = azureAdOptions.ClientId;
            microsoftOptions.TenantId = azureAdOptions.TenantId;
            microsoftOptions.Domain = azureAdOptions.Domain;
        }
    );

builder.Services.AddAuthorization(options =>
{
    // the default policy requires authentication via MCP
    options.FallbackPolicy = options.DefaultPolicy;
#if RequireAuthForRoles
    // Azure AD Group-Based Authorization Policies
    // To obtain group IDs: Azure Portal -> Microsoft Entra ID -> Groups -> Select group -> Copy Object ID

    // Policy for data analysts who can run analytical queries
    options.AddPolicy(
        Policies.DataAnalyst,
        policy =>
        {
            policy.RequireAuthenticatedUser();
            // Option 1: Use Azure AD group object ID (recommended for security)
            // policy.RequireClaim("groups", "12345678-1234-1234-1234-123456789012");

            // Option 2: Use application role (requires app roles configured in Entra App Registration)
            policy.RequireRole(Policies.DataAnalyst);

            // Option 3: Combine multiple groups with OR logic
            // policy.RequireAssertion(context =>
            //     context.User.HasClaim("groups", "data-analyst-group-id") ||
            //     context.User.HasClaim("groups", "admin-group-id"));
        }
    );
#endif
});
#endif

builder
    .Services.AddMcpServer(options =>
    {
        options.ServerInfo = new() { Name = "MCP Templates", Version = "0.7.0" };
    })
#if IncludeAuthentication
    .AddAuthorizationFilters()
#endif
    .WithHttpTransport(options =>
    {
        options.Stateless = true;
    })
    .WithToolsFromAssembly()
    .WithPromptsFromAssembly()
    .WithResourcesFromAssembly();

builder.Services.AddHttpContextAccessor();

// you can also add tools, prompts, and resources from
// other assemblies or add them manually as needed with the overloads of WithTools, WithPrompts, and WithResources

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

app.UseCors();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseForwardedHeaders();
#if IncludeAuthentication
app.UseAuthentication();
app.UseAuthorization();
#endif

app.UseHttpsRedirection();

#if IncludeAuthentication
app.MapMcp("mcp").RequireAuthorization();
#else
app.MapMcp("mcp");
#endif

#if IncludeDatabase
// Initialize database before starting the app
#if RequireDbForSampleData
await Dao.Aspire.Mcp.Data.DbInitializer.InitializeAsync(app.Services, seedData: true);
#else
await Dao.Aspire.Mcp.Data.DbInitializer.InitializeAsync(app.Services, seedData: false);
#endif
#endif

app.Run();

public partial class Program;
