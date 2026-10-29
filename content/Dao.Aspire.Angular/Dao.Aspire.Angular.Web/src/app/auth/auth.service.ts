import { HttpBackend, HttpClient, HttpErrorResponse, HttpHeaders } from '@angular/common/http';
import { inject, Injectable, signal } from '@angular/core';
import { firstValueFrom } from 'rxjs';
import { AUTH_PROVIDER, AUTH_CONFIGURATION } from './auth-provider';

export interface AppUser { id: string; name: string | null; }

@Injectable({ providedIn: 'root' })
export class AuthService {
  private readonly provider = inject(AUTH_PROVIDER);
  private readonly configuration = inject(AUTH_CONFIGURATION);
  private readonly http = new HttpClient(inject(HttpBackend));
  readonly user = signal<AppUser | null>(null);
  readonly error = signal<string | null>(null);

  async initialize(): Promise<void> {
    try {
      if (new URLSearchParams(window.location.search).has('authError')) {
        this.error.set('Sign-in failed or was cancelled. Try again.');
        window.history.replaceState({}, '', '/');
      }
      await this.provider.initialize(this.configuration, () => {
        this.user.set(null);
        this.error.set('Your session expired. Sign in again.');
      });
      if (!this.provider.hasSession()) return;
      const token = await this.provider.accessToken();
      const headers = token ? new HttpHeaders({ Authorization: `Bearer ${token}` }) : undefined;
      try {
        this.user.set(await firstValueFrom(this.http.get<AppUser>('/api/auth/me', { headers })));
      } catch (error) {
//#if (GoogleCookie)
        // A missing cookie session is the expected initial state.
        if (!(error instanceof HttpErrorResponse) || error.status !== 401) throw error;
//#else
        throw error;
//#endif
      }
    } catch (error) {
      this.fail(error);
    }
  }

  async login(): Promise<void> {
    this.error.set(null);
    try { await this.provider.login(); } catch (error) { this.fail(error); }
  }

  async logout(): Promise<void> {
    try {
//#if (GoogleCookie)
      const { token } = await firstValueFrom(this.http.get<{ token: string }>('/api/auth/csrf'));
      await firstValueFrom(this.http.post('/api/auth/logout', {}, {
        headers: { 'X-XSRF-TOKEN': token },
      }));
//#elseif (GoogleJwt)
      const token = await this.provider.accessToken();
      await firstValueFrom(this.http.post('/api/auth/logout', {}, {
        headers: { Authorization: `Bearer ${token}` },
      }));
//#endif
      await this.provider.logout();
      this.user.set(null);
    } catch (error) {
      this.fail(error);
    }
  }

  accessToken(): Promise<string> { return this.provider.accessToken(); }
  async csrfToken(): Promise<string> {
    return (await firstValueFrom(this.http.get<{ token: string }>('/api/auth/csrf'))).token;
  }
  private fail(error: unknown): void {
    console.error('Authentication failed', error);
    this.error.set(error instanceof Error ? error.message : 'Authentication failed. Check provider setup.');
  }
}
