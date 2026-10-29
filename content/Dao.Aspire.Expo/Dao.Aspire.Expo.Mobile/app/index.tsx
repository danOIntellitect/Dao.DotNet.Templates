import { useEffect, useState } from 'react';
import { ActivityIndicator, FlatList, StyleSheet, Text, View } from 'react-native';
import { StatusBar } from 'expo-status-bar';
import { getWeatherForecast, type WeatherForecast } from '../src/services/api.service';

export default function HomeScreen() {
  const [forecasts, setForecasts] = useState<WeatherForecast[]>([]);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    getWeatherForecast()
      .then(setForecasts)
      .catch((err: Error) => setError(err.message))
      .finally(() => setLoading(false));
  }, []);

  return (
    <View style={styles.container}>
      <Text style={styles.title}>Weather Forecast</Text>
      <Text style={styles.subtitle}>Live data from the C# API</Text>
      {loading && <ActivityIndicator size="large" color="#058743" />}
      {error && <Text style={styles.error}>{error}</Text>}
      <FlatList
        data={forecasts}
        keyExtractor={(item) => item.date}
        renderItem={({ item }) => (
          <View style={styles.card}>
            <Text style={styles.date}>{item.date}</Text>
            <Text>{item.temperatureC}°C / {item.temperatureF}°F</Text>
            <Text style={styles.summary}>{item.summary}</Text>
          </View>
        )}
      />
      <StatusBar style="auto" />
    </View>
  );
}

const styles = StyleSheet.create({
  container: { flex: 1, backgroundColor: '#fff', padding: 24, paddingTop: 56 },
  title: { fontSize: 28, fontWeight: '700', color: '#1a1a1a', marginBottom: 4 },
  subtitle: { fontSize: 14, color: '#666', marginBottom: 24 },
  error: { color: '#c62828', textAlign: 'center', marginTop: 16 },
  card: {
    backgroundColor: '#f5f5f5',
    borderRadius: 8,
    padding: 16,
    marginBottom: 12,
  },
  date: { fontWeight: '600', marginBottom: 4 },
  summary: { color: '#058743', marginTop: 4 },
});
