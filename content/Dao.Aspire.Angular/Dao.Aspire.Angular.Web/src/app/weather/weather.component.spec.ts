import { TestBed } from '@angular/core/testing';
import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { WeatherComponent } from './weather.component';

describe('WeatherComponent', () => {
  beforeEach(() => TestBed.configureTestingModule({
    imports: [WeatherComponent],
    providers: [provideHttpClient(), provideHttpClientTesting()],
  }));

  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('renders the records returned by the API', async () => {
    const fixture = TestBed.createComponent(WeatherComponent);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Loading forecasts');
    TestBed.inject(HttpTestingController).expectOne('/api/weatherforecast').flush([
      { date: '2026-01-01', temperatureC: 20, temperatureF: 67, summary: 'Mild' },
    ]);
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelectorAll('tbody tr')).toHaveLength(1);
    expect(fixture.nativeElement.querySelector('tbody').textContent).toContain('Mild');
  });

  it('shows an error and stops loading when the API fails', async () => {
    const fixture = TestBed.createComponent(WeatherComponent);
    fixture.detectChanges();
    TestBed.inject(HttpTestingController).expectOne('/api/weatherforecast')
      .flush('Unavailable', { status: 503, statusText: 'Service Unavailable' });
    await fixture.whenStable();
    expect(fixture.nativeElement.querySelector('.error').textContent)
      .toContain('Failed to load weather data');
    expect(fixture.nativeElement.querySelector('.loading')).toBeNull();
  });
});
