using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Identity.Web;

namespace Dao.Aspire.Angular.Api.Authentication.Entra;

public static class EntraAuthentication
{
    public static void AddEntraAuthentication(this WebApplicationBuilder builder)
    {
        if (!Guid.TryParse(builder.Configuration["AzureAd:TenantId"], out _) ||
            !Guid.TryParse(builder.Configuration["AzureAd:ClientId"], out _) ||
            !Guid.TryParse(builder.Configuration["Authentication:SpaClientId"], out _))
        {
            throw new InvalidOperationException("Set AzureAd:TenantId, AzureAd:ClientId (API), and Authentication:SpaClientId. See docs/authentication.md.");
        }
        builder.Configuration["AzureAd:Instance"] ??= "https://login.microsoftonline.com/";
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddMicrosoftIdentityWebApi(builder.Configuration.GetSection("AzureAd"));
        builder.Services.AddAuthorizationBuilder().AddPolicy("api", policy =>
            policy.RequireAuthenticatedUser().RequireScope("access_as_user"));
#if (IncludeSignalR)
        builder.Services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
        {
            var previous = options.Events.OnMessageReceived;
            options.Events.OnMessageReceived = async context =>
            {
                if (previous is not null) await previous(context);
                if (string.IsNullOrEmpty(context.Token) &&
                    context.Request.Path.StartsWithSegments("/hubs/notifications") &&
                    context.Request.Query.TryGetValue("access_token", out var token))
                {
                    context.Token = token;
                }
            };
        });
#endif
    }
}
