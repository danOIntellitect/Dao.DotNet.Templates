import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
//#if (IncludeSignalR || IncludeAuthentication)
import { signal } from '@angular/core';
//#endif
//#if (IncludeSignalR)
import { SignalrService } from './signalr/signalr.service';
//#endif
import { App } from './app';
//#if (IncludeAuthentication)
import { AuthService } from './auth/auth.service';
//#endif

describe('App', () => {
  beforeEach(async () => {
    await TestBed.configureTestingModule({
      imports: [App],
      providers: [
        provideHttpClient(),
        provideHttpClientTesting(),
//#if (IncludeAuthentication)
        { provide: AuthService, useValue: {
          user: signal({ id: 'test-user', name: 'Test User' }),
          error: signal(null),
          login: vi.fn(),
          logout: vi.fn(),
        } },
//#endif
//#if (IncludeSignalR)
        { provide: SignalrService, useValue: { notifications: signal<string[]>([]), error: signal(null) } },
//#endif
      ],
    }).compileComponents();
  });

  it('should create the app', () => {
    const fixture = TestBed.createComponent(App);
    const app = fixture.componentInstance;
    expect(app).toBeTruthy();
  });

  it('should render title', async () => {
    const fixture = TestBed.createComponent(App);
    fixture.detectChanges();
    TestBed.inject(HttpTestingController).expectOne('/api/weatherforecast').flush([]);
    await fixture.whenStable();
    const compiled = fixture.nativeElement as HTMLElement;
    expect(compiled.querySelector('h1')?.textContent).toContain('Hello, Dao.Aspire.Angular.Web');
    TestBed.inject(HttpTestingController).verify();
  });
});
