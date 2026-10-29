import { Injectable } from '@angular/core';
import { InMemoryWebStorage, UserManager, WebStorageStateStore } from 'oidc-client-ts';
import { AuthenticationProvider, AuthConfiguration } from './auth-provider';

@Injectable()
export class JwtProvider implements AuthenticationProvider {
  private manager!: UserManager;
  private signedIn = false;

  async initialize(configuration: AuthConfiguration, sessionExpired: () => void): Promise<void> {
    this.manager = new UserManager({
      authority: configuration.authority,
      client_id: configuration.clientId,
      redirect_uri: `${configuration.origin}/auth/callback`,
      response_type: 'code',
      scope: `openid profile offline_access ${configuration.scope}`,
      automaticSilentRenew: true,
      loadUserInfo: false,
      userStore: new WebStorageStateStore({ store: new InMemoryWebStorage() }),
      // Only the short-lived authorization transaction survives the full-page redirect.
      stateStore: new WebStorageStateStore({ store: window.sessionStorage }),
    });
    this.manager.events.addUserLoaded(() => { this.signedIn = true; });
    this.manager.events.addAccessTokenExpired(() => {
      this.signedIn = false;
      sessionExpired();
    });
    this.manager.events.addSilentRenewError((error) => {
      console.error('Session renewal failed', error);
      this.signedIn = false;
      sessionExpired();
    });
    if (window.location.pathname === '/auth/callback') {
      await this.manager.signinRedirectCallback();
      window.history.replaceState({}, '', '/');
    }
    this.signedIn = (await this.manager.getUser()) !== null;
  }

  hasSession(): boolean { return this.signedIn; }
  async login(): Promise<void> { await this.manager.signinRedirect(); }
  async logout(): Promise<void> {
    await this.manager.revokeTokens(['refresh_token', 'access_token']);
    await this.manager.removeUser();
    this.signedIn = false;
  }
  async accessToken(): Promise<string> {
    let user = await this.manager.getUser();
    if (user?.expired) user = await this.manager.signinSilent();
    if (!user || user.expired) throw new Error('Your session expired. Sign in again.');
    return user.access_token;
  }
}
