import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { parseSite, type SiteFieldsValue } from "../../lib/site";

interface Props {
  value: SiteFieldsValue;
  onChange: (v: SiteFieldsValue) => void;
  idPrefix: string;
}

/** Optional site data: coordinates (weather and map) and built area. */
export function ProjectSiteFields({ value, onChange, idPrefix }: Props) {
  const parsed = parseSite(value);
  const error = "error" in parsed ? parsed.error : null;
  return (
    <div className="space-y-2 rounded-md border border-border bg-muted/20 p-3">
      <p className="text-xs font-medium">Dados do terreno <span className="font-normal text-muted-foreground">(opcional)</span></p>
      <div className="grid grid-cols-3 gap-2">
        <div className="space-y-1">
          <Label htmlFor={`${idPrefix}-lat`} className="text-xs">Latitude</Label>
          <Input id={`${idPrefix}-lat`} inputMode="decimal" value={value.latitude} placeholder="-23.5505"
            onChange={(e) => onChange({ ...value, latitude: e.target.value })} className="text-sm" />
        </div>
        <div className="space-y-1">
          <Label htmlFor={`${idPrefix}-lon`} className="text-xs">Longitude</Label>
          <Input id={`${idPrefix}-lon`} inputMode="decimal" value={value.longitude} placeholder="-46.6333"
            onChange={(e) => onChange({ ...value, longitude: e.target.value })} className="text-sm" />
        </div>
        <div className="space-y-1">
          <Label htmlFor={`${idPrefix}-area`} className="text-xs">Área (m²)</Label>
          <Input id={`${idPrefix}-area`} inputMode="decimal" value={value.areaM2} placeholder="250"
            onChange={(e) => onChange({ ...value, areaM2: e.target.value })} className="text-sm" />
        </div>
      </div>
      <p className="text-[11px] text-muted-foreground">
        Coordenadas em graus decimais (latitude entre -90 e 90, longitude entre -180 e 180). Dica: no Google Maps,
        clique com o botão direito no terreno para copiar. Usadas para o clima do diário.
      </p>
      {error && <p role="alert" className="text-xs text-destructive">{error}</p>}
    </div>
  );
}
