import { Injectable } from '@angular/core';
import { AuthenticationProvider } from './auth-provider';

@Injectable()
export class CookieProvider implements AuthenticationProvider {
  async initialize(): Promise<void> {}
  hasSession(): boolean { return true; }
  async login(): Promise<void> { window.location.assign('/api/auth/login'); }
  async logout(): Promise<void> {}
  async accessToken(): Promise<string> { return ''; }
}
