import assert from 'node:assert/strict';
import { afterEach, mock, test } from 'node:test';

process.env.EXPO_PUBLIC_API_URL = 'http://localhost:5123';
const { getWeatherForecast } = await import('./api.service.ts');

afterEach(() => mock.restoreAll());

test('uses the Aspire API URL and returns forecasts', async () => {
  const forecasts = [{ date: '2026-01-01', temperatureC: 20, temperatureF: 67, summary: 'Mild' }];
  const fetchMock = mock.method(globalThis, 'fetch', async () =>
    new Response(JSON.stringify(forecasts), { status: 200 }),
  );
  assert.deepEqual(await getWeatherForecast(), forecasts);
  assert.equal(fetchMock.mock.calls[0].arguments[0], 'http://localhost:5123/api/weatherforecast');
});

test('reports an unsuccessful API response', async () => {
  mock.method(globalThis, 'fetch', async () => new Response(null, { status: 503 }));
  await assert.rejects(getWeatherForecast(), /API error: 503/);
});

test('propagates network errors for the screen to display', async () => {
  mock.method(globalThis, 'fetch', async () => { throw new Error('Network unavailable'); });
  await assert.rejects(getWeatherForecast(), /Network unavailable/);
});
