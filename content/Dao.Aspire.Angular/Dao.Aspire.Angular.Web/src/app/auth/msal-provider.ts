import { inject, Injectable } from '@angular/core';
import { MsalService } from '@azure/msal-angular';
import { firstValueFrom } from 'rxjs';
import { AuthenticationProvider, AuthConfiguration } from './auth-provider';

@Injectable()
export class MsalProvider implements AuthenticationProvider {
  private readonly msal = inject(MsalService);
  private configuration!: AuthConfiguration;

  async initialize(configuration: AuthConfiguration): Promise<void> {
    this.configuration = configuration;
    await this.msal.instance.initialize();
    const result = await firstValueFrom(this.msal.handleRedirectObservable());
    const account = result?.account ?? this.msal.instance.getActiveAccount() ??
      this.msal.instance.getAllAccounts()[0] ?? null;
    this.msal.instance.setActiveAccount(account);
  }
  hasSession(): boolean { return this.msal.instance.getActiveAccount() !== null; }
  async login(): Promise<void> {
    await firstValueFrom(this.msal.loginRedirect({ scopes: [this.configuration.scope] }));
  }
  async logout(): Promise<void> { await firstValueFrom(this.msal.logoutRedirect()); }
  async accessToken(): Promise<string> {
    const result = await firstValueFrom(this.msal.acquireTokenSilent({ scopes: [this.configuration.scope] }));
    return result.accessToken;
  }
}
