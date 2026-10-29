# Authentication setup

This project protects `/api/weatherforecast` and, when included, the
`/hubs/notifications` hub and `/api/notify` endpoint. The frontend shell, health
probes, and authentication protocol endpoints remain public. A frontend route
guard is not an API security boundary: authorization is enforced by the API.

## Local HTTPS and configuration

Trust the .NET development certificate before starting:

```powershell
dotnet dev-certs https --trust
```

Run `aspire start` from the generated solution directory. For authenticated
variants Aspire runs the Angular HTTPS dev server at **https://localhost:4200**.
The `start:auth` script exports your existing development certificate into the
ignored `.certificates` directory; never commit that directory or its private
key. If port 4200 is occupied, stop the specific conflicting app or change the
AppHost endpoint, `Authentication:PublicOrigin`, and all registered redirect URIs
together. Multiple authenticated templates need different ports.

The Angular proxy preserves the public Host and forwards `/api`, `/signin-google`,
`/connect`, `/.well-known`, and optional `/hubs` to the API. Open the **frontend**
URL, not the API resource URL, for sign-in. Production serves frontend and API on
the same HTTPS origin. Public frontend auth configuration comes from
`/api/auth/config`; secrets are never returned by that endpoint.

Set secrets against the **API** project:

```powershell
dotnet user-secrets set "Authentication:PublicOrigin" "https://localhost:4200" --project Dao.Aspire.Angular.Api
```

Missing required settings fail API startup with an actionable error. Frontend
setup, sign-in, and renewal failures are displayed rather than silently granting
anonymous access.

<!--#if (Google)-->
## Register Google sign-in

1. Create/select a project in [Google Cloud Console](https://console.cloud.google.com/).
2. Configure Google Auth Platform branding, audience/consent screen, and contact
   information. For an external app in Testing, add the Google accounts that
   will test sign-in to the test-user list. Follow Google's verification
   requirements before making the consent screen available publicly.
3. Create an OAuth client of type **Web application**, not Android or desktop.
4. Register the exact authorized redirect URI:
   **https://localhost:4200/signin-google**. The server-side handler does not
   require loading the Google browser SDK or configuring a JavaScript origin.
5. Store the client ID and secret on the API:

```powershell
dotnet user-secrets set "Authentication:Google:ClientId" "<web-client-id>" --project Dao.Aspire.Angular.Api
dotnet user-secrets set "Authentication:Google:ClientSecret" "<client-secret>" --project Dao.Aspire.Angular.Api
```

Only basic identity/profile/email scopes are requested. No Google API access is
granted to callers of your API. Google's ID token proves identity; a Google API
access token is not an access token for this application's API.

For production add `https://your-public-host/signin-google` to that OAuth client's
redirect URIs (prefer a separate production client). Set
`Authentication__PublicOrigin`, `Authentication__Google__ClientId`, and
`Authentication__Google__ClientSecret` through deployment configuration/secrets.
Never put the client secret into Angular, `appsettings.json`, or a template CLI
argument captured in shell history.

`redirect_uri_mismatch` means the public scheme/host/port/path does not match
Google's registered value exactly. A blocked consent screen generally means a
test user, publishing status, organization policy, or verification requirement
needs attention. A cancelled login returns a visible error; it does not create
a session.
<!--#endif-->

<!--#if (GoogleCookie)-->
## GoogleCookie: server-owned session

ASP.NET Core's Google handler completes sign-in and creates a Secure, HttpOnly,
SameSite=Lax application cookie. Angular requests `/api/auth/me` for the
server-validated user; JavaScript cannot read the session credential.
The session lasts eight hours with sliding expiration. Sign-out is an
authenticated POST to `/api/auth/logout`.

The API validates an antiforgery token on state-changing authenticated API
requests and SignalR negotiation. Angular's interceptor and SignalR service
obtain that token from `/api/auth/csrf` and send `X-XSRF-TOKEN`. Do not replace
logout with a GET or remove CSRF protection when adding mutation endpoints.
API 401/403 responses do not redirect to Google.

Cookies suit this same-origin web app and do not require a user database.
Choose a token-based mode for native/mobile clients or independently hosted APIs.
There is no custom JWT/refresh service in this variant.
<!--#endif-->

<!--#if (GoogleJwt)-->
## GoogleJwt: Google identity, application-issued access JWTs

This API also hosts an OpenIddict authorization server. Google sign-in creates
an ASP.NET Core Identity session. OpenIddict validates the public Angular client's
authorization-code/PKCE exchange and issues short-lived, signed application
access JWTs for audience/scope `api`. The registered client is `angular`, with
the exact redirect **https://localhost:4200/auth/callback**. Register this
callback with OpenIddict (the startup registration uses PublicOrigin), **not**
with Google: Google's callback is `/signin-google`.

`oidc-client-ts` owns PKCE, state/nonce validation, callback processing, and token
renewal. Access/refresh tokens are kept in memory, not localStorage/sessionStorage.
Only the temporary authorization transaction uses sessionStorage across the
full-page redirect. Reloading loses browser tokens: click Sign in again; the
server/Google session can avoid asking for credentials again.

The template requests `openid profile offline_access api`. Access tokens last
10 minutes and refresh tokens last seven days. OpenIddict stores token records,
rotates refresh tokens with no reuse grace period, and rejects invalid/redeemed tokens using its supported
handlers. The local API checks token and authorization entries, so revocation
is effective without waiting for a JWT to expire. Sign-out uses an
API-bearer-authenticated POST (cookies alone cannot authorize it), revokes the user's application
authorizations (all sessions for this starter), clears the server session, and
removes the browser's tokens. Google itself remains signed in.
Bearer-authorized sign-out also works after the browser's server session cookie
expires; automatic cookies cannot forge the required Authorization header.

Identity users are linked by Google's stable provider key, **not by matching
email**. No password flow, roles, local registration, or automatic account
linking is generated.

### Database and migrations

The default SQLite database is `authentication.db` in the API working directory.
It stores Identity users/logins and OpenIddict applications/authorizations/tokens;
it contains sensitive authentication data and must be backed up and protected.
Development applies the checked-in initial migration automatically. Install
`dotnet-ef` matching EF Core 10 if you do not already have it:

```powershell
dotnet tool install --global dotnet-ef --version 10.*
dotnet ef database update --project Dao.Aspire.Angular.Api
```

Production does **not** apply schema changes automatically: apply migrations in
your deployment process before starting the API. Set
`ConnectionStrings__Authentication` to a durable, writable SQLite path for a
single-instance deployment. Container-local ephemeral storage is not durable.
Do not run multiple replicas against separate SQLite files. For a scaled
deployment, replace the SQLite provider with an appropriate shared production
database, update the context/design-time factory, and generate provider-specific
migrations; do not reuse SQLite migrations for another database.

### Signing and encryption credentials

Development uses OpenIddict's persisted development certificates. Production
requires **separate signing and encryption PFX certificates**, distinct from
your TLS certificate:

```text
Authentication__SigningCertificate__Path=/mounted-secrets/signing.pfx
Authentication__SigningCertificate__Password=<secret>
Authentication__EncryptionCertificate__Path=/mounted-secrets/encryption.pfx
Authentication__EncryptionCertificate__Password=<secret>
```

Use your platform's secret/certificate store and mount the files read-only.
Never commit private keys. Extend credential registration with overlapping old
and new certificates during key rotation so outstanding tokens remain usable
until their expiry. Back up encryption credentials: losing them invalidates
outstanding encrypted codes/refresh tokens.

The OpenID discovery document is
`https://localhost:4200/.well-known/openid-configuration`; protocol endpoints
include `/connect/authorize`, `/connect/token`, and `/connect/revoke`.
The issuer must remain the configured public HTTPS origin through a reverse
proxy. Never disable HTTPS, issuer, audience, PKCE, or signature checks to
work around deployment configuration.
<!--#endif-->

<!--#if (MSAL)-->
## Register Microsoft Entra applications

This variant targets **one workforce tenant**. It does not configure B2C,
External ID, personal Microsoft accounts, or multi-tenant onboarding.

1. In the [Entra admin center](https://entra.microsoft.com/), create an **API**
   app registration, selecting accounts in this organizational directory only.
2. Under **Expose an API**, set the Application ID URI to
   `api://<api-client-id>` and add delegated scope `access_as_user`.
   Set the API's requested access token version to **2** in its manifest
   (`api.requestedAccessTokenVersion`).
3. Create a separate **SPA** registration in the same tenant. Add a
   **Single-page application** platform redirect of **https://localhost:4200**
   (use exactly that value in configuration and portal registration).
   Do not create a client secret for the SPA or enable implicit grant.
4. Under the SPA's API permissions, add the API's delegated
   `api://<api-client-id>/access_as_user` permission. Grant consent as required
   by the tenant's policies.
5. Configure the API, keeping the SPA and API client IDs distinct:

```powershell
dotnet user-secrets set "AzureAd:TenantId" "<directory-tenant-id>" --project Dao.Aspire.Angular.Api
dotnet user-secrets set "AzureAd:ClientId" "<api-client-id>" --project Dao.Aspire.Angular.Api
dotnet user-secrets set "Authentication:SpaClientId" "<spa-client-id>" --project Dao.Aspire.Angular.Api
```

MSAL Angular handles initialization, redirects, accounts, API token acquisition,
and its interceptor. MSAL's supported cache defaults here use sessionStorage.
Never store a confidential credential in the SPA. The API uses
Microsoft.Identity.Web to validate the token and requires `access_as_user`;
a valid Microsoft Graph token or ID token is not sufficient.

In production configure `AzureAd__TenantId`, `AzureAd__ClientId`,
`Authentication__SpaClientId`, and `Authentication__PublicOrigin`. Register that
exact public origin as the SPA redirect/logout destination. No Entra client
secret is required for this SPA-to-API flow.

Consent errors require permission/tenant policy changes, not bypassing scope
checks. Audience errors usually indicate that the SPA requested a Graph token or
used the SPA client ID where the API client ID belongs. An expired session or
Conditional Access interaction may require signing in again.
<!--#endif-->

## Production and operational checks

- Configure the public HTTPS origin and provider redirects before deployment.
  Behind a reverse proxy, explicitly configure trusted proxy addresses/networks
  using ASP.NET Core's ForwardedHeadersOptions. The template retains framework
  trust restrictions; never clear them to trust arbitrary forwarded headers.
  Set production AllowedHosts to the public hostname.
- Persist/share ASP.NET Core Data Protection keys across restarts/replicas.
  Protect them at rest using the deployment platform's supported key storage.
  Otherwise authentication/correlation cookies may become invalid after
  redeployment or when another replica handles a callback.
- Keep health checks anonymous. Protect all new business API endpoints with the
  `api` policy. Do not put an authorization fallback policy on OAuth callbacks
  or discovery endpoints.
- SignalR, when included, uses the selected auth method, reconnects only while
  signed in, and stops on sign-out. Bearer query tokens are accepted only on
  `/hubs/notifications`. Ensure reverse-proxy/access logging redacts query
  tokens and Authorization/Cookie headers.
- Verify sign-in, refresh/expiry where applicable, a protected weather request,
  SignalR notification, sign-out, rejected anonymous access, and cancelled
  sign-in using real Google/Entra accounts after registration. Automated local
  provider tests cannot verify your organization's cloud consent configuration.

References: [Google server-side sign-in](https://learn.microsoft.com/aspnet/core/security/authentication/social/google-logins),
[Google token verification](https://developers.google.com/identity/gsi/web/guides/verify-google-id-token),
[OpenIddict](https://documentation.openiddict.com/),
[MSAL Angular](https://github.com/AzureAD/microsoft-authentication-library-for-js/tree/dev/lib/msal-angular),
[Microsoft.Identity.Web](https://learn.microsoft.com/entra/identity-platform/scenario-protected-web-api-app-configuration).
