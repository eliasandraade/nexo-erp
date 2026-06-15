import { apiClient } from "@/services/api-client";

export interface WeatherResult {
  latitude: number;
  longitude: number;
  date: string;
  temperatureMax: number;
  temperatureMin: number;
  precipitationMm: number;
  weatherCode: number;
  description: string;
  summary: string; // formatted: "28°C / 22°C · Ensolarado · Chuva: 0.0mm"
}

interface WeatherResponse {
  found: boolean;
  data: WeatherResult | null;
  unavailable?: boolean;
}

export async function getWeatherCurrent(lat: number, lon: number): Promise<WeatherResponse> {
  return apiClient.get<WeatherResponse>(`/integrations/weather/current?lat=${lat}&lon=${lon}`);
}

export async function getWeatherHistory(lat: number, lon: number, date: string): Promise<WeatherResponse> {
  return apiClient.get<WeatherResponse>(`/integrations/weather/history?lat=${lat}&lon=${lon}&date=${date}`);
}
