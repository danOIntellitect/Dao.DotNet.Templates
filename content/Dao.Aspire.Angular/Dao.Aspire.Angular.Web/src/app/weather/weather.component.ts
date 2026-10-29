import { Component, inject, signal, OnInit, ChangeDetectionStrategy } from '@angular/core';
import { WeatherService, WeatherForecast } from './weather.service';

@Component({
  selector: 'app-weather',
  standalone: true,
  template: `
    <section class="weather">
      <h2>Weather Forecast</h2>
      <p class="subtitle">Live data from the C# API — <code>GET /api/weatherforecast</code></p>
      @if (loading()) {
        <p class="loading">Loading forecasts...</p>
      } @else if (error()) {
        <p class="error">{{ error() }}</p>
      } @else {
        <table>
          <thead>
            <tr>
              <th>Date</th>
              <th>Temp (°C)</th>
              <th>Temp (°F)</th>
              <th>Summary</th>
            </tr>
          </thead>
          <tbody>
            @for (forecast of forecasts(); track forecast.date) {
              <tr>
                <td>{{ forecast.date }}</td>
                <td>{{ forecast.temperatureC }}</td>
                <td>{{ forecast.temperatureF }}</td>
                <td>{{ forecast.summary }}</td>
              </tr>
            }
          </tbody>
        </table>
      }
    </section>
  `,
  changeDetection: ChangeDetectionStrategy.Eager,
  styles: `
    .weather {
      width: 100%;
      max-width: 700px;
      margin: 2rem auto;
    }

    h2 {
      color: var(--accent, #058743);
      font-size: 1.5rem;
      margin-bottom: 0.25rem;
    }

    .subtitle {
      color: #666;
      font-size: 0.85rem;
      margin-bottom: 1rem;
    }

    .subtitle code {
      background: #f0f0f0;
      padding: 0.1rem 0.4rem;
      border-radius: 0.25rem;
      font-size: 0.8rem;
    }

    table {
      width: 100%;
      border-collapse: collapse;
      font-size: 0.9rem;
    }

    th {
      background: var(--accent, #058743);
      color: #fff;
      padding: 0.625rem 0.75rem;
      text-align: left;
    }

    td {
      padding: 0.5rem 0.75rem;
      border-bottom: 1px solid #e0e0e0;
    }

    tr:hover td {
      background: color-mix(in srgb, var(--accent, #058743) 8%, transparent);
    }

    .loading, .error {
      text-align: center;
      padding: 1rem;
    }

    .error {
      color: #c62828;
    }
  `,
})
export class WeatherComponent implements OnInit {
  private readonly weatherService = inject(WeatherService);

  readonly forecasts = signal<WeatherForecast[]>([]);
  readonly loading = signal(true);
  readonly error = signal<string | null>(null);

  ngOnInit(): void {
    this.weatherService.getForecasts().subscribe({
      next: (data) => {
        this.forecasts.set(data);
        this.loading.set(false);
      },
      error: () => {
        this.error.set('Failed to load weather data. Is the API running?');
        this.loading.set(false);
      },
    });
  }
}
