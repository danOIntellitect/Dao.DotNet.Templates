using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace Dao.Aspire.Angular.Api.Authentication.GoogleJwt;

public static class GoogleJwtEndpoints
{
    public static void MapGoogleJwtEndpoints(this WebApplication app)
    {
        app.MapGet("/connect/authorize", AuthorizeAsync).AllowAnonymous();
        app.MapPost("/connect/token", ExchangeAsync).AllowAnonymous().DisableAntiforgery();
        app.MapGet("/api/auth/google-callback", GoogleCallbackAsync).AllowAnonymous();
        app.MapPost("/api/auth/logout", LogoutAsync).RequireAuthorization("api");
    }

    private static async Task<IResult> AuthorizeAsync(HttpContext context, UserManager<IdentityUser> users,
        IOpenIddictApplicationManager applications, IOpenIddictAuthorizationManager authorizations,
        SignInManager<IdentityUser> signInManager)
    {
        var request = context.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("Missing OpenIddict request.");
        var session = await context.AuthenticateAsync(TemplateAuthentication.SessionScheme);
        var user = session.Succeeded ? await users.GetUserAsync(session.Principal!) : null;
        if (user is not null && (!await signInManager.CanSignInAsync(user) || await users.IsLockedOutAsync(user)))
            return ProtocolError(Errors.AccessDenied);
        var requestKey = RequestKey(context.Request.QueryString.Value!);
        var freshLogin = session.Properties?.Items.TryGetValue("authorization_request", out var previousRequest) == true &&
            previousRequest == requestKey;
        if (user is null || (!freshLogin && (request.HasPromptValue(PromptValues.Login) ||
            (request.MaxAge is long maxAge && session.Properties?.IssuedUtc is DateTimeOffset issued &&
             DateTimeOffset.UtcNow - issued > TimeSpan.FromSeconds(maxAge)))))
        {
            if (request.HasPromptValue(PromptValues.None)) return ProtocolError(Errors.LoginRequired);
            var returnUrl = context.Request.Path + context.Request.QueryString;
            return Results.Challenge(signInManager.ConfigureExternalAuthenticationProperties("Google",
                QueryHelpers.AddQueryString("/api/auth/google-callback", "returnUrl", returnUrl!)), ["Google"]);
        }

        var application = await applications.FindByClientIdAsync(request.ClientId!)
            ?? throw new InvalidOperationException("Unregistered OIDC client.");
        var identity = new ClaimsIdentity(TokenValidationParameters.DefaultAuthenticationType, Claims.Name, Claims.Role);
        identity.SetClaim(Claims.Subject, user.Id)
            .SetClaim(Claims.Name, session.Principal!.FindFirstValue("display_name") ?? user.UserName);
        var principal = new ClaimsPrincipal(identity);
        principal.SetScopes(request.GetScopes());
        principal.SetResources("api");
        // A separate authorization per login allows refresh-token reuse/logout to invalidate its token family.
        var authorization = await authorizations.CreateAsync(principal, user.Id,
            (await applications.GetIdAsync(application))!, AuthorizationTypes.Permanent, principal.GetScopes());
        principal.SetAuthorizationId(await authorizations.GetIdAsync(authorization));
        SetDestinations(principal);
        return Results.SignIn(principal, authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private static async Task<IResult> GoogleCallbackAsync(HttpContext context, string? returnUrl,
        SignInManager<IdentityUser> signInManager, UserManager<IdentityUser> users, ILoggerFactory loggerFactory)
    {
        if (returnUrl is null || !returnUrl.StartsWith("/connect/authorize?", StringComparison.Ordinal))
            return Results.BadRequest(new { error = "Invalid login return URL." });
        var info = await signInManager.GetExternalLoginInfoAsync();
        if (info is null) return Results.Redirect("/?authError=google");
        var user = await users.FindByLoginAsync(info.LoginProvider, info.ProviderKey);
        if (user is null)
        {
            user = new IdentityUser { UserName = $"google-{Guid.NewGuid():N}" };
            var created = await users.CreateAsync(user);
            if (!created.Succeeded) return IdentityFailure(created);
            var linked = await users.AddLoginAsync(user, info);
            if (!linked.Succeeded)
            {
                var deleted = await users.DeleteAsync(user);
                if (!deleted.Succeeded) loggerFactory.CreateLogger("GoogleAuthentication")
                    .LogError("Unable to clean up an unlinked external user: {Errors}", deleted.Errors);
                return IdentityFailure(linked);
            }
        }
        if (!await signInManager.CanSignInAsync(user) || await users.IsLockedOutAsync(user))
        {
            await context.SignOutAsync(IdentityConstants.ExternalScheme);
            loggerFactory.CreateLogger("GoogleAuthentication").LogWarning("Rejected sign-in for an unavailable account.");
            return Results.Redirect("/?authError=google");
        }
        var principal = await signInManager.CreateUserPrincipalAsync(user);
        var name = info.Principal.FindFirstValue(ClaimTypes.Name) ?? user.UserName!;
        ((ClaimsIdentity)principal.Identity!).AddClaim(new Claim("display_name", name));
        await context.SignOutAsync(IdentityConstants.ExternalScheme);
        var properties = new AuthenticationProperties();
        properties.Items["authorization_request"] = RequestKey(new Uri("https://localhost" + returnUrl).Query);
        await context.SignInAsync(TemplateAuthentication.SessionScheme, principal, properties);
        return Results.LocalRedirect(returnUrl);

        IResult IdentityFailure(IdentityResult result)
        {
            loggerFactory.CreateLogger("GoogleAuthentication").LogError(
                "Unable to provision Google-linked user: {Errors}", result.Errors);
            return Results.Problem("Unable to provision the Google-linked account.", statusCode: 500);
        }
    }

    private static async Task<IResult> ExchangeAsync(HttpContext context, UserManager<IdentityUser> users,
        SignInManager<IdentityUser> signInManager)
    {
        var request = context.GetOpenIddictServerRequest()
            ?? throw new InvalidOperationException("Missing OpenIddict request.");
        if (!request.IsAuthorizationCodeGrantType() && !request.IsRefreshTokenGrantType())
            return ProtocolError(Errors.UnsupportedGrantType);
        var result = await context.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        var principal = result.Principal;
        var user = principal is null ? null : await users.FindByIdAsync(principal.GetClaim(Claims.Subject)!);
        if (user is null) return ProtocolError(Errors.InvalidGrant);
        if (!await signInManager.CanSignInAsync(user) || await users.IsLockedOutAsync(user))
            return ProtocolError(Errors.InvalidGrant);
        SetDestinations(principal!);
        return Results.SignIn(principal!, authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    private static async Task<IResult> LogoutAsync(HttpContext context,
        IOpenIddictAuthorizationManager authorizations, ILoggerFactory loggerFactory)
    {
        var subject = context.User.GetClaim(Claims.Subject)!;
        await foreach (var authorization in authorizations.FindAsync(
            subject: subject, client: null, status: Statuses.Valid, type: null, scopes: null))
        {
            if (!await authorizations.TryRevokeAsync(authorization, context.RequestAborted))
            {
                loggerFactory.CreateLogger("GoogleAuthentication").LogError("Unable to revoke an application authorization.");
                return Results.Problem("Unable to complete sign-out. Try again.", statusCode: 500);
            }
        }
        await context.SignOutAsync(TemplateAuthentication.SessionScheme);
        return Results.NoContent();
    }

    private static IResult ProtocolError(string error) => Results.Forbid(
        new AuthenticationProperties(new Dictionary<string, string?>
        {
            [OpenIddictServerAspNetCoreConstants.Properties.Error] = error
        }), [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);

    private static string RequestKey(string query) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(query)));

    private static void SetDestinations(ClaimsPrincipal principal) => principal.SetDestinations(claim => claim.Type switch
    {
        Claims.Name when principal.HasScope(Scopes.Profile) => [Destinations.AccessToken, Destinations.IdentityToken],
        Claims.Subject => [Destinations.AccessToken, Destinations.IdentityToken],
        _ => []
    });
}
