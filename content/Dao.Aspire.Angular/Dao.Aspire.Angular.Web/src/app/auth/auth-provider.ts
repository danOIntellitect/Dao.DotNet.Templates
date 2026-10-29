import { InjectionToken } from '@angular/core';

export interface AuthConfiguration {
  authority: string;
  clientId: string;
  scope: string;
  origin: string;
}

export interface AuthenticationProvider {
  initialize(configuration: AuthConfiguration, sessionExpired: () => void): Promise<void>;
  hasSession(): boolean;
  login(): Promise<void>;
  logout(): Promise<void>;
  accessToken(): Promise<string>;
}

export const AUTH_PROVIDER = new InjectionToken<AuthenticationProvider>('AuthenticationProvider');
export const AUTH_CONFIGURATION = new InjectionToken<AuthConfiguration>('AuthConfiguration');
