using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.HttpOverrides;
#if (GoogleJwt)
using Dao.Aspire.Angular.Api.Authentication.GoogleJwt;
#endif
#if (MSAL)
using Dao.Aspire.Angular.Api.Authentication.Entra;
#endif

namespace Dao.Aspire.Angular.Api.Authentication;

public static class TemplateAuthentication
{
#if (GoogleJwt)
    public static readonly string SessionScheme = Microsoft.AspNetCore.Identity.IdentityConstants.ApplicationScheme;
#else
    public const string SessionScheme = "ApplicationSession";
#endif

    public static void AddTemplateAuthentication(this WebApplicationBuilder builder)
    {
        var origin = builder.Configuration["Authentication:PublicOrigin"]
            ?? (builder.Environment.IsDevelopment() ? "https://localhost:4200" : null);
        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri) ||
            uri.Scheme != "https" || uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment) || !string.IsNullOrEmpty(uri.UserInfo))
        {
            throw new InvalidOperationException("Set Authentication:PublicOrigin to the public HTTPS origin (without a path). See docs/authentication.md.");
        }
        builder.Services.AddSingleton(new PublicOrigin(uri.GetLeftPart(UriPartial.Authority)));
        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            // Retain the framework's trusted-proxy restrictions.
        });
        builder.Services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-XSRF-TOKEN";
            options.Cookie.Name = "__Host-Dao.Antiforgery";
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        });

#if (Google)
        var clientId = builder.Configuration["Authentication:Google:ClientId"];
        var clientSecret = builder.Configuration["Authentication:Google:ClientSecret"];
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            throw new InvalidOperationException("Set Authentication:Google:ClientId and ClientSecret using user secrets or deployment secrets. See docs/authentication.md.");
        }
#endif
#if (GoogleCookie)
        builder.Services.AddAuthentication(SessionScheme)
            .AddCookie(SessionScheme, ConfigureSessionCookie)
            .AddGoogle(options =>
            {
                options.ClientId = clientId;
                options.ClientSecret = clientSecret;
                options.SignInScheme = SessionScheme;
                options.Events.OnRemoteFailure = context =>
                {
                    context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                        .CreateLogger("GoogleAuthentication").LogWarning(context.Failure, "Google sign-in failed.");
                    context.Response.Redirect("/?authError=google");
                    context.HandleResponse();
                    return Task.CompletedTask;
                };
            });
        builder.Services.AddAuthorizationBuilder().AddPolicy("api", policy =>
            policy.AddAuthenticationSchemes(SessionScheme).RequireAuthenticatedUser());
#endif
#if (GoogleJwt)
        builder.AddGoogleJwt(clientId, clientSecret, uri);
#endif
#if (MSAL)
        builder.AddEntraAuthentication();
#endif
    }

    public static void ConfigureSessionCookie(CookieAuthenticationOptions options)
    {
        options.Cookie.Name = "__Host-Dao.Session";
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Events.OnRedirectToLogin = context =>
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return Task.CompletedTask;
        };
        options.Events.OnRedirectToAccessDenied = context =>
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return Task.CompletedTask;
        };
    }

    public static async Task InitializeTemplateAuthenticationAsync(this WebApplication app)
    {
#if (GoogleJwt)
        await app.InitializeGoogleJwtAsync();
#else
        await Task.CompletedTask;
#endif
    }

    public static void MapTemplateAuthentication(this WebApplication app)
    {
#if (GoogleCookie)
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/hubs") && context.WebSockets.IsWebSocketRequest &&
                context.Request.Headers.Origin != context.RequestServices.GetRequiredService<PublicOrigin>().Value)
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }
            if ((context.Request.Path.StartsWithSegments("/api") || context.Request.Path.StartsWithSegments("/hubs")) &&
                !HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method) &&
                !HttpMethods.IsOptions(context.Request.Method) && context.User.Identity?.IsAuthenticated == true)
            {
                if (!await ValidateAntiforgeryAsync(context)) return;
            }
            await next(context);
        });
#endif
        app.MapGet("/api/auth/csrf", (HttpContext context, IAntiforgery antiforgery) =>
        {
            context.Response.Headers.CacheControl = "no-store";
            var tokens = antiforgery.GetAndStoreTokens(context);
            return Results.Ok(new { token = tokens.RequestToken });
        })
#if (GoogleJwt)
        .RequireAuthorization("session");
#else
        .AllowAnonymous();
#endif

        app.MapGet("/api/auth/me", (HttpContext context) => Results.Ok(new
        {
            id = context.User.FindFirstValue("sub") ?? context.User.FindFirstValue(ClaimTypes.NameIdentifier),
            name = context.User.Identity?.Name ?? context.User.FindFirstValue("name"),
        })).RequireAuthorization("api");

        app.MapGet("/api/auth/config", (HttpContext context, PublicOrigin origin, IConfiguration configuration) =>
        {
            context.Response.Headers.CacheControl = "no-store";
#if (MSAL)
            return Results.Ok(new
            {
                authority = $"https://login.microsoftonline.com/{configuration["AzureAd:TenantId"]}",
                clientId = configuration["Authentication:SpaClientId"],
                scope = $"api://{configuration["AzureAd:ClientId"]}/access_as_user",
                origin = origin.Value
            });
#else
            return Results.Ok(new { authority = origin.Value, clientId = "angular", scope = "api", origin = origin.Value });
#endif
        }).AllowAnonymous();

#if (GoogleCookie)
        app.MapGet("/api/auth/login", () => Results.Challenge(
            new AuthenticationProperties { RedirectUri = "/" }, ["Google"])).AllowAnonymous();
        app.MapPost("/api/auth/logout", async (HttpContext context) =>
        {
            await context.SignOutAsync(SessionScheme);
            return Results.NoContent();
        }).RequireAuthorization("api");
#endif
#if (GoogleJwt)
        app.MapGoogleJwtEndpoints();
#endif
    }

    public static async Task<bool> ValidateAntiforgeryAsync(HttpContext context)
    {
        try
        {
            await context.RequestServices.GetRequiredService<IAntiforgery>().ValidateRequestAsync(context);
            return true;
        }
        catch (AntiforgeryValidationException exception)
        {
            context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("Antiforgery")
                .LogWarning(exception, "Rejected request with an invalid antiforgery token.");
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await context.Response.WriteAsJsonAsync(new { error = "Invalid antiforgery token. Reload the page and try again." });
            return false;
        }
    }
}

public sealed record PublicOrigin(string Value);
