using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using OpenIddict.Validation;
using OpenIddict.Validation.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Dao.Aspire.Angular.Api.Authentication.GoogleJwt;

public static class GoogleJwtAuthentication
{
    public static void AddGoogleJwt(this WebApplicationBuilder builder, string clientId, string clientSecret, Uri origin)
    {
        builder.Services.AddDbContext<AuthDbContext>(options =>
        {
            options.UseSqlite(builder.Configuration.GetConnectionString("Authentication") ?? "Data Source=authentication.db");
            options.UseOpenIddict();
        });
        builder.Services.AddIdentityCore<IdentityUser>()
            .AddEntityFrameworkStores<AuthDbContext>()
            .AddSignInManager();
        builder.Services.AddAuthentication(options =>
        {
            options.DefaultScheme = OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme;
        })
            .AddCookie(TemplateAuthentication.SessionScheme, options =>
            {
                TemplateAuthentication.ConfigureSessionCookie(options);
                options.Events.OnValidatePrincipal = SecurityStampValidator.ValidatePrincipalAsync;
            })
            .AddCookie(IdentityConstants.ExternalScheme, options =>
            {
                options.Cookie.Name = "__Host-Dao.External";
                options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
                options.ExpireTimeSpan = TimeSpan.FromMinutes(5);
            })
            .AddGoogle(options =>
            {
                options.ClientId = clientId;
                options.ClientSecret = clientSecret;
                options.SignInScheme = IdentityConstants.ExternalScheme;
                options.Events.OnRemoteFailure = context =>
                {
                    context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>()
                        .CreateLogger("GoogleAuthentication").LogWarning(context.Failure, "Google sign-in failed.");
                    context.Response.Redirect("/?authError=google");
                    context.HandleResponse();
                    return Task.CompletedTask;
                };
            });
        builder.Services.AddOpenIddict()
            .AddCore(options => options.UseEntityFrameworkCore().UseDbContext<AuthDbContext>())
            .AddServer(options =>
            {
                options.SetIssuer(origin);
                options.SetAuthorizationEndpointUris("/connect/authorize");
                options.SetTokenEndpointUris("/connect/token");
                options.SetRevocationEndpointUris("/connect/revoke");
                options.AllowAuthorizationCodeFlow().AllowRefreshTokenFlow().RequireProofKeyForCodeExchange();
                options.RegisterScopes(Scopes.OpenId, Scopes.Profile, Scopes.OfflineAccess, "api");
                options.SetAccessTokenLifetime(TimeSpan.FromMinutes(10));
                options.SetRefreshTokenLifetime(TimeSpan.FromDays(7));
                options.SetRefreshTokenReuseLeeway(TimeSpan.Zero);
                options.DisableSlidingRefreshTokenExpiration();
                // Access JWTs are signed; authorization codes and refresh tokens remain encrypted.
                options.DisableAccessTokenEncryption();
                if (builder.Environment.IsDevelopment())
                {
                    options.AddDevelopmentSigningCertificate().AddDevelopmentEncryptionCertificate();
                }
                else
                {
                    options.AddSigningCertificate(LoadCertificate(builder.Configuration, "Signing"));
                    options.AddEncryptionCertificate(LoadCertificate(builder.Configuration, "Encryption"));
                }
                options.UseAspNetCore().EnableAuthorizationEndpointPassthrough().EnableTokenEndpointPassthrough();
            })
            .AddValidation(options =>
            {
                options.UseLocalServer();
                options.AddAudiences("api");
                options.EnableTokenEntryValidation();
                options.EnableAuthorizationEntryValidation();
                options.UseAspNetCore()
                    .DisableAccessTokenExtractionFromBodyForm()
                    .DisableAccessTokenExtractionFromQueryString();
#if (IncludeSignalR)
                options.AddEventHandler<OpenIddictValidationEvents.ProcessAuthenticationContext>(handler =>
                    handler.UseInlineHandler(context =>
                    {
                        var request = context.Transaction.GetHttpRequest();
                        if (request is not null && request.Path.StartsWithSegments("/hubs/notifications") &&
                            string.IsNullOrEmpty(context.AccessToken))
                        {
                            context.AccessToken = request.Query["access_token"];
                        }
                        return ValueTask.CompletedTask;
                    }).SetOrder(OpenIddictValidationAspNetCoreHandlers.ExtractAccessTokenFromAuthorizationHeader.Descriptor.Order + 500));
#endif
            });
        builder.Services.AddAuthorizationBuilder()
            .AddPolicy("api", policy => policy
                .AddAuthenticationSchemes(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)
                .RequireAuthenticatedUser().RequireAssertion(context => context.User.HasScope("api")))
            .AddPolicy("session", policy => policy
                .AddAuthenticationSchemes(TemplateAuthentication.SessionScheme).RequireAuthenticatedUser());
    }

    public static async Task InitializeGoogleJwtAsync(this WebApplication app)
    {
        await using var scope = app.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AuthDbContext>();
        if (app.Environment.IsDevelopment()) await database.Database.MigrateAsync();
        else if ((await database.Database.GetPendingMigrationsAsync()).Any())
            throw new InvalidOperationException("Apply authentication database migrations before starting in Production.");

        var origin = app.Services.GetRequiredService<PublicOrigin>().Value;
        var applications = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
        var descriptor = new OpenIddictApplicationDescriptor
        {
            ClientId = "angular",
            ClientType = ClientTypes.Public,
            ConsentType = ConsentTypes.Implicit,
            DisplayName = "Angular frontend",
            RedirectUris = { new Uri($"{origin}/auth/callback") },
            Permissions =
            {
                Permissions.Endpoints.Authorization, Permissions.Endpoints.Token, Permissions.Endpoints.Revocation,
                Permissions.GrantTypes.AuthorizationCode, Permissions.GrantTypes.RefreshToken,
                Permissions.ResponseTypes.Code, Permissions.Scopes.Profile, Permissions.Prefixes.Scope + "api"
            },
            Requirements = { Requirements.Features.ProofKeyForCodeExchange }
        };
        var application = await applications.FindByClientIdAsync("angular");
        if (application is null) await applications.CreateAsync(descriptor);
        else await applications.UpdateAsync(application, descriptor);
    }

    private static X509Certificate2 LoadCertificate(IConfiguration configuration, string purpose)
    {
        var path = configuration[$"Authentication:{purpose}Certificate:Path"];
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException($"Configure Authentication:{purpose}Certificate:Path and Password for Production.");
        return X509CertificateLoader.LoadPkcs12FromFile(path, configuration[$"Authentication:{purpose}Certificate:Password"]);
    }
}
