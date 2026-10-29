import { ApplicationConfig, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideHttpClient, withXhr } from '@angular/common/http';
import { provideRouter } from '@angular/router';

import { routes } from './app.routes';
//#if (IncludeAuthentication)
import { withInterceptors, withInterceptorsFromDi } from '@angular/common/http';
import { authenticationProviders, authInterceptor } from './auth/auth.config';
//#endif

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
//#if (IncludeAuthentication)
    provideHttpClient(withXhr(), withInterceptors([authInterceptor]), withInterceptorsFromDi()),
    ...authenticationProviders,
//#else
    provideHttpClient(withXhr()),
//#endif
    provideRouter(routes)
  ]
};
