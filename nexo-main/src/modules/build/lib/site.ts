import { parseDecimal } from "./format";

export interface SiteFieldsValue {
  latitude:  string;
  longitude: string;
  areaM2:    string;
}

export const EMPTY_SITE: SiteFieldsValue = { latitude: "", longitude: "", areaM2: "" };

export function siteFromProject(p: { latitude?: number | null; longitude?: number | null; areaM2?: number | null }): SiteFieldsValue {
  return {
    latitude:  p.latitude  != null ? String(p.latitude)  : "",
    longitude: p.longitude != null ? String(p.longitude) : "",
    areaM2:    p.areaM2    != null ? String(p.areaM2)    : "",
  };
}

export interface ParsedSite {
  latitude:  number | null;
  longitude: number | null;
  areaM2:    number | null;
}

/** Validates the optional site fields; returns the error message (pt-BR) or the parsed values. */
export function parseSite(v: SiteFieldsValue): { error: string } | { value: ParsedSite } {
  const lat  = parseDecimal(v.latitude);
  const lon  = parseDecimal(v.longitude);
  const area = parseDecimal(v.areaM2);
  if (lat !== null && (Number.isNaN(lat) || lat < -90 || lat > 90))
    return { error: "Latitude inválida: use um valor entre -90 e 90." };
  if (lon !== null && (Number.isNaN(lon) || lon < -180 || lon > 180))
    return { error: "Longitude inválida: use um valor entre -180 e 180." };
  if ((lat === null) !== (lon === null))
    return { error: "Informe latitude e longitude juntas (ou nenhuma das duas)." };
  if (area !== null && (Number.isNaN(area) || area < 0))
    return { error: "A área precisa ser um número maior ou igual a zero." };
  return { value: { latitude: lat, longitude: lon, areaM2: area } };
}
