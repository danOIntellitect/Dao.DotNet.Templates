import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { AUTH_CONFIGURATION, AUTH_PROVIDER } from './auth-provider';
import { AuthService } from './auth.service';

describe('AuthService', () => {
  const provider = {
    initialize: vi.fn().mockResolvedValue(undefined),
    hasSession: vi.fn().mockReturnValue(true),
    accessToken: vi.fn().mockResolvedValue('test-access-token'),
    login: vi.fn().mockResolvedValue(undefined),
    logout: vi.fn().mockResolvedValue(undefined),
  };
  let service: AuthService;
  let http: HttpTestingController;

  beforeEach(() => {
    vi.clearAllMocks();
    provider.hasSession.mockReturnValue(true);
    TestBed.configureTestingModule({
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
        { provide: AUTH_PROVIDER, useValue: provider },
        { provide: AUTH_CONFIGURATION, useValue: {
          authority: 'https://localhost:4200', clientId: 'test', scope: 'api', origin: 'https://localhost:4200',
        } },
      ],
    });
    service = TestBed.inject(AuthService);
    http = TestBed.inject(HttpTestingController);
  });
  afterEach(() => http.verify());

  it('loads the server-validated user after provider initialization', async () => {
    const initialized = service.initialize();
    await vi.waitFor(() => {
      const request = http.expectOne('/api/auth/me');
      expect(request.request.headers.get('Authorization')).toBe('Bearer test-access-token');
      request.flush({ id: 'user-id', name: 'Test User' });
    });
    await initialized;
    expect(service.user()?.name).toBe('Test User');
    expect(service.error()).toBeNull();
  });

  it('does not request protected data without a token session', async () => {
    provider.hasSession.mockReturnValue(false);
    await service.initialize();
    http.expectNone('/api/auth/me');
    expect(service.user()).toBeNull();
  });

  it('handles an unauthorized current-user request without granting access', async () => {
    const initialized = service.initialize();
    await vi.waitFor(() => http.expectOne('/api/auth/me').flush({}, { status: 401, statusText: 'Unauthorized' }));
    await initialized;
    expect(service.user()).toBeNull();
//#if (GoogleCookie)
    expect(service.error()).toBeNull();
//#else
    expect(service.error()).toBeTruthy();
//#endif
  });

  it('delegates interactive sign-in to the provider SDK', async () => {
    await service.login();
    expect(provider.login).toHaveBeenCalledOnce();
  });

  it('clears local user state only after logout succeeds', async () => {
    service.user.set({ id: 'test', name: 'Test User' });
    const logout = service.logout();
//#if (GoogleCookie)
    http.expectOne('/api/auth/csrf').flush({ token: 'csrf-token' });
    await vi.waitFor(() => {
      const request = http.expectOne('/api/auth/logout');
      expect(request.request.method).toBe('POST');
      expect(request.request.headers.get('X-XSRF-TOKEN')).toBe('csrf-token');
      request.flush(null, { status: 204, statusText: 'No Content' });
    });
//#elseif (GoogleJwt)
    await vi.waitFor(() => {
      const request = http.expectOne('/api/auth/logout');
      expect(request.request.headers.get('Authorization')).toBe('Bearer test-access-token');
      request.flush(null, { status: 204, statusText: 'No Content' });
    });
//#endif
    await logout;
    expect(provider.logout).toHaveBeenCalledOnce();
    expect(service.user()).toBeNull();
  });
});
