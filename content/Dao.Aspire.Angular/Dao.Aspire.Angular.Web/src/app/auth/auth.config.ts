import { HttpInterceptorFn } from '@angular/common/http';
import { inject, provideAppInitializer } from '@angular/core';
import { from, switchMap } from 'rxjs';
import { AUTH_PROVIDER, AUTH_CONFIGURATION } from './auth-provider';
import { AuthService } from './auth.service';
//#if (GoogleCookie)
import { CookieProvider } from './cookie-provider';
//#endif
//#if (GoogleJwt)
import { JwtProvider } from './jwt-provider';
//#endif
//#if (MSAL)
import { HTTP_INTERCEPTORS } from '@angular/common/http';
import {
  MSAL_INSTANCE, MSAL_INTERCEPTOR_CONFIG, MsalBroadcastService,
  MsalInterceptor, MsalService,
} from '@azure/msal-angular';
import { BrowserCacheLocation, InteractionType, PublicClientApplication } from '@azure/msal-browser';
import { MsalProvider } from './msal-provider';
//#endif

export const authenticationProviders = [
//#if (GoogleCookie)
  { provide: AUTH_PROVIDER, useClass: CookieProvider },
//#endif
//#if (GoogleJwt)
  { provide: AUTH_PROVIDER, useClass: JwtProvider },
//#endif
//#if (MSAL)
  {
    provide: MSAL_INSTANCE,
    useFactory: () => {
      const config = inject(AUTH_CONFIGURATION);
      return new PublicClientApplication({
        auth: {
          clientId: config.clientId, authority: config.authority,
          redirectUri: config.origin, postLogoutRedirectUri: config.origin,
        },
        cache: { cacheLocation: BrowserCacheLocation.SessionStorage },
      });
    },
  },
  { provide: MSAL_INTERCEPTOR_CONFIG, useFactory: () => {
    const config = inject(AUTH_CONFIGURATION);
    return {
      interactionType: InteractionType.Redirect,
      protectedResourceMap: new Map<string, string[] | null>([
        ['/api/auth/config', null],
        ['/api/auth/csrf', null],
        [`${config.origin}/api/auth/config`, null],
        [`${config.origin}/api/auth/csrf`, null],
        ['/api/*', [config.scope]],
        [`${config.origin}/api/*`, [config.scope]],
      ]),
    };
  } },
  { provide: HTTP_INTERCEPTORS, useClass: MsalInterceptor, multi: true },
  MsalService,
  MsalBroadcastService,
  { provide: AUTH_PROVIDER, useClass: MsalProvider },
//#endif
  provideAppInitializer(async () => {
    const service = inject(AuthService);
    await service.initialize();
  }),
];

export const authInterceptor: HttpInterceptorFn = (request, next) => {
  const url = new URL(request.url, window.location.origin);
  if (url.origin !== window.location.origin || !url.pathname.startsWith('/api/')) return next(request);
  const auth = inject(AuthService);
//#if (GoogleCookie)
  if (['GET', 'HEAD', 'OPTIONS'].includes(request.method)) return next(request);
  return from(auth.csrfToken()).pipe(switchMap(token =>
    next(request.clone({ setHeaders: { 'X-XSRF-TOKEN': token } }))));
//#elseif (GoogleJwt)
  if (!auth.user()) return next(request);
  return from(auth.accessToken()).pipe(switchMap(token =>
    next(request.clone({ setHeaders: { Authorization: `Bearer ${token}` } }))));
//#else
  return next(request);
//#endif
};
