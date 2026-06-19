import { useEffect, useState } from "react";
import { Check, Clock, Eye, CalendarCheck, AlertCircle, Loader2, Globe2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import {
  Select, SelectContent, SelectItem, SelectTrigger, SelectValue,
} from "@/components/ui/select";
import { cn } from "@/lib/utils";
import { useUpdatePublicBookingSettings } from "../hooks/usePublicBookingSettings";
import type { PublicBookingSettingsDto } from "../api/service.api";

const TIMEZONES = [
  "America/Sao_Paulo", "America/Fortaleza", "America/Recife", "America/Bahia",
  "America/Belem", "America/Manaus", "America/Cuiaba", "America/Campo_Grande",
  "America/Porto_Velho", "America/Rio_Branco", "America/Noronha",
];
const TZ_LABEL: Record<string, string> = {
  "America/Sao_Paulo": "Brasília / São Paulo (UTC−3)",
  "America/Fortaleza": "Fortaleza — Nordeste (UTC−3)",
  "America/Recife": "Recife (UTC−3)",
  "America/Bahia": "Salvador (UTC−3)",
  "America/Belem": "Belém (UTC−3)",
  "America/Manaus": "Manaus (UTC−4)",
  "America/Cuiaba": "Cuiabá (UTC−4)",
  "America/Campo_Grande": "Campo Grande (UTC−4)",
  "America/Porto_Velho": "Porto Velho (UTC−4)",
  "America/Rio_Branco": "Rio Branco (UTC−5)",
  "America/Noronha": "Fernando de Noronha (UTC−2)",
};

const DAYS_AHEAD = [7, 14, 30, 60, 90];
const LEAD_OPTIONS: { value: number; label: string }[] = [
  { value: 0, label: "Sem antecedência" },
  { value: 30, label: "30 minutos" },
  { value: 60, label: "1 hora" },
  { value: 120, label: "2 horas" },
  { value: 240, label: "4 horas" },
  { value: 720, label: "12 horas" },
  { value: 1440, label: "1 dia" },
];
const SLOT_OPTIONS = [15, 20, 30, 45, 60];

interface FormState {
  publicBookingEnabled: boolean;
  bookingDaysAhead: number;
  minLeadMinutes: number;
  slotIntervalMinutes: number;
  showPrices: boolean;
  autoConfirmAppointments: boolean;
  timeZoneId: string;
}

function toForm(s: PublicBookingSettingsDto): FormState {
  return {
    publicBookingEnabled: s.publicBookingEnabled,
    bookingDaysAhead: s.bookingDaysAhead,
    minLeadMinutes: s.minLeadMinutes,
    slotIntervalMinutes: s.slotIntervalMinutes,
    showPrices: s.showPrices,
    autoConfirmAppointments: s.autoConfirmAppointments,
    timeZoneId: s.timeZoneId,
  };
}

export function PortalBookingSettingsForm({ settings }: { settings: PublicBookingSettingsDto }) {
  const [form, setForm] = useState<FormState>(() => toForm(settings));
  useEffect(() => { setForm(toForm(settings)); }, [settings]);
  const set = <K extends keyof FormState>(k: K, v: FormState[K]) => setForm((f) => ({ ...f, [k]: v }));

  const mut = useUpdatePublicBookingSettings();

  // Ensure the stored tz always appears even if it's outside the curated list.
  const tzOptions = TIMEZONES.includes(form.timeZoneId) ? TIMEZONES : [form.timeZoneId, ...TIMEZONES];

  return (
    <div className="space-y-4">
      <Toggle
        checked={form.publicBookingEnabled}
        onChange={(v) => set("publicBookingEnabled", v)}
        icon={CalendarCheck}
        label="Agendamento público ativo"
        description={form.publicBookingEnabled
          ? "Clientes podem agendar pelo link público."
          : "O portal fica indisponível para novos agendamentos."}
      />

      <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
        <Field label="Fuso horário" icon={Globe2} hint="Base dos horários de atendimento.">
          <Select value={form.timeZoneId} onValueChange={(v) => set("timeZoneId", v)}>
            <SelectTrigger><SelectValue /></SelectTrigger>
            <SelectContent>
              {tzOptions.map((tz) => (
                <SelectItem key={tz} value={tz}>{TZ_LABEL[tz] ?? tz}</SelectItem>
              ))}
            </SelectContent>
          </Select>
        </Field>

        <Field label="Dias futuros disponíveis" hint="Até quando o cliente pode agendar.">
          <Select value={String(form.bookingDaysAhead)} onValueChange={(v) => set("bookingDaysAhead", Number(v))}>
            <SelectTrigger><SelectValue /></SelectTrigger>
            <SelectContent>
              {[...new Set([form.bookingDaysAhead, ...DAYS_AHEAD])].sort((a, b) => a - b).map((d) => (
                <SelectItem key={d} value={String(d)}>{d} dias</SelectItem>
              ))}
            </SelectContent>
          </Select>
        </Field>

        <Field label="Antecedência mínima" icon={Clock} hint="Tempo mínimo entre agora e o horário.">
          <Select value={String(form.minLeadMinutes)} onValueChange={(v) => set("minLeadMinutes", Number(v))}>
            <SelectTrigger><SelectValue /></SelectTrigger>
            <SelectContent>
              {LEAD_OPTIONS.map((o) => (
                <SelectItem key={o.value} value={String(o.value)}>{o.label}</SelectItem>
              ))}
            </SelectContent>
          </Select>
        </Field>

        <Field label="Intervalo dos horários" hint="Passo entre horários ofertados.">
          <Select value={String(form.slotIntervalMinutes)} onValueChange={(v) => set("slotIntervalMinutes", Number(v))}>
            <SelectTrigger><SelectValue /></SelectTrigger>
            <SelectContent>
              {[...new Set([form.slotIntervalMinutes, ...SLOT_OPTIONS])].sort((a, b) => a - b).map((m) => (
                <SelectItem key={m} value={String(m)}>{m} minutos</SelectItem>
              ))}
            </SelectContent>
          </Select>
        </Field>
      </div>

      <Toggle
        checked={form.showPrices}
        onChange={(v) => set("showPrices", v)}
        icon={Eye}
        label="Mostrar preços"
        description="Exibe o preço de cada serviço no portal público."
      />
      <Toggle
        checked={form.autoConfirmAppointments}
        onChange={(v) => set("autoConfirmAppointments", v)}
        icon={CalendarCheck}
        label="Confirmar automaticamente"
        description="Agendamentos do portal já entram como confirmados (em vez de aguardando)."
      />

      <div className="flex items-center gap-3 pt-1">
        <Button
          onClick={() => mut.mutate({
            publicBookingEnabled: form.publicBookingEnabled,
            bookingDaysAhead: form.bookingDaysAhead,
            minLeadMinutes: form.minLeadMinutes,
            slotIntervalMinutes: form.slotIntervalMinutes,
            showPrices: form.showPrices,
            autoConfirmAppointments: form.autoConfirmAppointments,
            timeZoneId: form.timeZoneId,
          })}
          disabled={mut.isPending}
        >
          {mut.isPending ? <><Loader2 className="mr-2 h-4 w-4 animate-spin" />Salvando...</> : "Salvar configurações"}
        </Button>
        {mut.isSuccess && <span className="flex items-center gap-1.5 text-sm text-primary"><Check className="h-4 w-4" /> Salvo</span>}
        {mut.isError && <span className="flex items-center gap-1.5 text-sm text-destructive"><AlertCircle className="h-4 w-4" /> Erro ao salvar</span>}
      </div>
    </div>
  );
}

// ── local helpers ───────────────────────────────────────────────────────────────

function Toggle({ checked, onChange, label, description, icon: Icon }: {
  checked: boolean; onChange: (v: boolean) => void; label: string; description: string; icon: React.ElementType;
}) {
  return (
    <button
      type="button"
      onClick={() => onChange(!checked)}
      className={cn(
        "flex w-full items-center gap-4 rounded-xl border p-4 text-left transition-colors",
        checked ? "border-primary/40 bg-primary/5" : "border-border bg-card hover:border-border/80",
      )}
    >
      <div className={cn("shrink-0 rounded-lg p-2", checked ? "bg-primary/10 text-primary" : "bg-muted text-muted-foreground")}>
        <Icon className="h-4 w-4" />
      </div>
      <div className="min-w-0 flex-1">
        <p className="text-sm font-medium">{label}</p>
        <p className="mt-0.5 text-xs text-muted-foreground">{description}</p>
      </div>
      <div className={cn("relative h-5 w-9 shrink-0 rounded-full transition-colors", checked ? "bg-primary" : "bg-muted")}>
        <div className={cn("absolute top-0.5 h-4 w-4 rounded-full bg-white shadow transition-transform", checked ? "translate-x-4" : "translate-x-0.5")} />
      </div>
    </button>
  );
}

function Field({ label, hint, icon: Icon, children }: {
  label: string; hint?: string; icon?: React.ElementType; children: React.ReactNode;
}) {
  return (
    <div className="space-y-1.5">
      <label className="flex items-center gap-1.5 text-xs font-medium uppercase tracking-wide text-muted-foreground">
        {Icon && <Icon className="h-3.5 w-3.5" />} {label}
      </label>
      {children}
      {hint && <p className="text-xs text-muted-foreground">{hint}</p>}
    </div>
  );
}
