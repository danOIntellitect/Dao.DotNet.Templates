import { bootstrapApplication } from '@angular/platform-browser';
import { appConfig } from './app/app.config';
import { App } from './app/app';
//#if (IncludeAuthentication)
import { AUTH_CONFIGURATION, AuthConfiguration } from './app/auth/auth-provider';

async function bootstrap(): Promise<void> {
  const response = await fetch('/api/auth/config');
  if (!response.ok) throw new Error('Unable to load authentication configuration. Check the API logs and docs/authentication.md.');
  const configuration: AuthConfiguration = await response.json();
  await bootstrapApplication(App, {
    ...appConfig,
    providers: [{ provide: AUTH_CONFIGURATION, useValue: configuration }, ...appConfig.providers],
  });
}
bootstrap().catch(error => {
  console.error(error);
  const root = document.querySelector('app-root');
  if (root) root.textContent = 'Authentication setup failed. Check the API logs and docs/authentication.md.';
});
//#else
bootstrapApplication(App, appConfig)
  .catch((err) => console.error(err));
//#endif
