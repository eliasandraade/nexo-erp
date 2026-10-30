import {
  Cloud, CloudDrizzle, CloudFog, CloudLightning, CloudRain, CloudSun, Droplets, MapPin, Snowflake, Sun, Wind, type LucideIcon,
} from "lucide-react";
import { cn } from "@/lib/utils";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import type { BuildWeatherDayDto, BuildWeatherDto } from "../../api/build-forecast.api";
import { useProjectWeather } from "../../hooks/use-build-forecast";
import { fmtWeekday, severityMeta, weatherIcon, weatherRiskLabel, type WeatherIconKey } from "../../lib/forecast";

const ICONS: Record<WeatherIconKey, LucideIcon> = {
  sun: Sun, "cloud-sun": CloudSun, cloud: Cloud, fog: CloudFog, drizzle: CloudDrizzle, rain: CloudRain, storm: CloudLightning, snow: Snowflake,
};

const num = (v: number | null | undefined, digits = 0) => (v == null ? "—" : v.toLocaleString("pt-BR", { maximumFractionDigits: digits }));

function DayIcon({ day, className }: { day: BuildWeatherDayDto; className?: string }) {
  const Icon = ICONS[weatherIcon(day)];
  return <Icon className={className} aria-hidden />;
}

function LevelDot({ level }: { level: string }) {
  if (level !== "medium" && level !== "high") return null;
  return <span className={cn("inline-block h-2 w-2 rounded-full", level === "high" ? "bg-rose-500" : "bg-amber-500")}
    title={`Risco ${severityMeta(level).label.toLowerCase()} (previsão)`} />;
}

function Today({ day }: { day: BuildWeatherDayDto }) {
  return (
    <div className="flex flex-wrap items-center gap-3 rounded-md bg-muted/40 p-3" data-testid="weather-today">
      <DayIcon day={day} className="h-8 w-8 text-primary" />
      <div className="min-w-0 flex-1">
        <p className="text-sm font-semibold">Hoje · {day.description ?? "Previsão"}</p>
        <p className="text-xs text-muted-foreground">
          {num(day.temperatureMax)}° / {num(day.temperatureMin)}° · <Droplets className="inline h-3 w-3" aria-hidden /> {num(day.precipitationMm, 1)} mm
          {day.precipitationProbability != null ? ` (${day.precipitationProbability}%)` : ""} · <Wind className="inline h-3 w-3" aria-hidden /> {num(day.windSpeedMaxKmh)} km/h
        </p>
      </div>
      <LevelDot level={day.level} />
    </div>
  );
}

function Days({ days }: { days: BuildWeatherDayDto[] }) {
  return (
    <ul className="grid grid-cols-4 gap-1.5 sm:grid-cols-7" data-testid="weather-days">
      {days.slice(0, 7).map((d) => (
        <li key={d.date} className="flex min-w-0 flex-col items-center gap-0.5 rounded-md border border-border p-1.5 text-center">
          <span className="truncate text-[11px] text-muted-foreground">{fmtWeekday(d.date)}</span>
          <DayIcon day={d} className="h-5 w-5 text-muted-foreground" />
          <span className="text-xs font-medium tabular-nums">{num(d.temperatureMax)}°/{num(d.temperatureMin)}°</span>
          <span className="text-[11px] tabular-nums text-muted-foreground">{num(d.precipitationMm, 1)} mm</span>
          <LevelDot level={d.level} />
        </li>
      ))}
    </ul>
  );
}

function Risks({ weather }: { weather: BuildWeatherDto }) {
  const all = [weather.today, ...(weather.days ?? [])].filter((d): d is BuildWeatherDayDto => !!d)
    .flatMap((d) => (d.risks ?? []).map((r) => ({ ...r, date: d.date })));
  if (all.length === 0)
    return <p className="text-xs text-muted-foreground">Nenhum risco climático previsto para as etapas sensíveis em campo.</p>;
  return (
    <ul className="space-y-1.5" data-testid="weather-risks">
      {all.map((r) => (
        <li key={`${r.date}-${r.stageId}-${r.kind}`} className="space-y-0.5 rounded-md border border-border p-2 text-xs">
          <div className="flex flex-wrap items-center gap-2">
            <span className={cn("rounded-full px-2 py-0.5 text-[11px] font-medium", severityMeta(r.level).cls)}>
              {weatherRiskLabel(r.kind)} (previsão)
            </span>
            <span className="font-medium">{fmtWeekday(r.date)} · {r.stageName}</span>
          </div>
          <p className="break-words text-muted-foreground">{r.message}</p>
        </li>
      ))}
    </ul>
  );
}

/**
 * "Clima na obra": today and the next 7 days at the site (Open-Meteo), with the per-day risk for weather-sensitive stages in
 * the field — always worded as a forecast, never applied to the dates. Without coordinates, a CTA to edit the site.
 */
export function WeatherCard({ projectId, onEditSite }: { projectId: string; onEditSite?: () => void }) {
  const { data, isLoading, isError } = useProjectWeather(projectId);
  const weather = data && typeof data === "object" && !Array.isArray(data) ? data : null;
  const sensitive = (weather?.stages ?? []).filter((s) => s.sensitive);

  return (
    <section className="min-w-0 space-y-3 rounded-md border border-border bg-card p-3 sm:p-4" data-testid="weather-card" aria-label="Clima na obra">
      <h3 className="flex items-center gap-1.5 text-sm font-semibold">
        <CloudSun className="h-4 w-4 text-muted-foreground" aria-hidden /> Clima na obra
      </h3>

      {isLoading && <Skeleton className="h-20 w-full" />}
      {isError && <p role="alert" className="text-sm text-destructive">Não foi possível consultar o clima.</p>}

      {weather && !weather.available && (
        <div className="space-y-2" data-testid="weather-unavailable">
          <p className="text-sm text-muted-foreground">{weather.message ?? "Previsão do tempo indisponível."}</p>
          {weather.reason === "no_coordinates" && onEditSite && (
            <Button size="sm" variant="outline" onClick={onEditSite}>
              <MapPin className="mr-1.5 h-3.5 w-3.5" /> Informar a localização da obra
            </Button>
          )}
        </div>
      )}

      {weather?.available && (
        <>
          {weather.today && <Today day={weather.today} />}
          {(weather.days ?? []).length > 0 && <Days days={weather.days} />}
          <div className="space-y-1.5">
            <h4 className="text-xs font-semibold">Riscos para etapas sensíveis</h4>
            <Risks weather={weather} />
          </div>
        </>
      )}

      {weather && (
        <p className="text-[11px] text-muted-foreground">
          {sensitive.length > 0
            ? `Etapas sensíveis ao clima: ${sensitive.map((s) => s.name).join(", ")}. `
            : "Nenhuma etapa marcada como sensível ao clima. "}
          {weather.available ? `${weather.disclaimer} Fonte: ${weather.source}.` : ""}
        </p>
      )}
    </section>
  );
}
