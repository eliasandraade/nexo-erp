import { useState, type CSSProperties, type InputHTMLAttributes, type ReactNode } from "react";
import { AlertCircle, CalendarClock, Loader2 } from "lucide-react";
import { cn } from "@/lib/utils";
import type { PublicCatalogItem, ServiceLabels } from "../api/booking.api";
import { formatDuration, formatPrice, formatSlotLabel, isValidPhone } from "../lib/booking-format";
import { Btn, Surface, Muted } from "./PortalPrimitives";

export interface GuestFormValue {
  customerName: string;
  phone:        string;
  email:        string;
  subjectName:  string;
  subjectNotes: string;
  notes:        string;
}

interface GuestFormProps {
  service:          PublicCatalogItem;
  professionalName: string;
  slotIso:          string;
  labels:           ServiceLabels;
  showPrice:        boolean;
  requiresSubject:  boolean;
  submitting:       boolean;
  errorMessage:     string | null;
  onConfirm:        (value: GuestFormValue) => void;
}

export function GuestForm({
  service, professionalName, slotIso, labels, showPrice, requiresSubject, submitting, errorMessage, onConfirm,
}: GuestFormProps) {
  const [v, setV] = useState<GuestFormValue>({
    customerName: "", phone: "", email: "", subjectName: "", subjectNotes: "", notes: "",
  });
  const set = (p: Partial<GuestFormValue>) => setV((s) => ({ ...s, ...p }));

  const customerNoun = labels.customer.toLowerCase();
  const subjectNoun = labels.subject.toLowerCase();
  const phoneOk = isValidPhone(v.phone);
  const canSubmit =
    v.customerName.trim().length > 0 && phoneOk &&
    (!requiresSubject || v.subjectName.trim().length > 0) && !submitting;

  return (
    <div className="flex flex-col gap-5">
      {/* Summary */}
      <Surface className="p-4" style={{ background: "var(--p-accent-soft)", borderColor: "transparent" }}>
        <div className="flex items-start gap-3">
          <CalendarClock className="mt-0.5 h-5 w-5 shrink-0" style={{ color: "var(--p-accent)" }} />
          <div className="min-w-0 text-sm">
            <p className="font-semibold" style={{ fontFamily: "var(--p-display)" }}>{service.name}</p>
            <p style={{ color: "var(--p-muted)" }}>
              {professionalName} · {formatDuration(service.durationMinutes)}
              {showPrice && service.price !== null && <> · {formatPrice(service.price)}</>}
            </p>
            <p className="mt-1 font-semibold capitalize">{formatSlotLabel(slotIso)}</p>
          </div>
        </div>
      </Surface>

      <div className="flex flex-col gap-3">
        <Field label={`Nome do ${customerNoun} *`}>
          <Input value={v.customerName} onChange={(e) => set({ customerName: e.target.value })}
            placeholder="Nome completo" autoComplete="name" />
        </Field>
        <Field label="Telefone / WhatsApp *" error={v.phone.length > 0 && !phoneOk ? "Telefone inválido." : null}>
          <Input value={v.phone} onChange={(e) => set({ phone: e.target.value })}
            placeholder="(00) 00000-0000" type="tel" autoComplete="tel" />
        </Field>
        <Field label="E-mail (opcional)">
          <Input value={v.email} onChange={(e) => set({ email: e.target.value })}
            placeholder="voce@email.com" type="email" autoComplete="email" />
        </Field>

        {requiresSubject && (
          <>
            <Field label={`Nome do ${subjectNoun} *`}>
              <Input value={v.subjectName} onChange={(e) => set({ subjectName: e.target.value })}
                placeholder={`Identifique o ${subjectNoun}`} />
            </Field>
            <Field label={`Detalhes do ${subjectNoun} (opcional)`}>
              <Textarea value={v.subjectNotes} onChange={(e) => set({ subjectNotes: e.target.value })}
                placeholder="Raça, modelo, placa, observações…" />
            </Field>
          </>
        )}

        <Field label="Observações (opcional)">
          <Textarea value={v.notes} onChange={(e) => set({ notes: e.target.value })}
            placeholder="Algo que o profissional deva saber" />
        </Field>
      </div>

      {errorMessage && (
        <div className="flex items-start gap-2 rounded-[calc(var(--p-radius)*0.6)] px-3 py-2.5 text-sm"
          style={{ background: "color-mix(in srgb, #e5484d 12%, var(--p-surface))", color: "#c62a2f" }}>
          <AlertCircle className="mt-0.5 h-4 w-4 shrink-0" /> <span>{errorMessage}</span>
        </div>
      )}

      <Btn onClick={() => onConfirm(v)} disabled={!canSubmit} className="h-12 w-full text-[15px]">
        {submitting ? <Loader2 className="h-5 w-5 animate-spin" /> : "Confirmar agendamento"}
      </Btn>
    </div>
  );
}

function Field({ label, error, children }: { label: string; error?: string | null; children: ReactNode }) {
  return (
    <label className="flex flex-col gap-1.5">
      <span className="text-xs font-semibold" style={{ color: "var(--p-muted)" }}>{label}</span>
      {children}
      {error && <Muted className="text-xs" >{error}</Muted>}
    </label>
  );
}

const fieldStyle: CSSProperties = {
  background: "var(--p-surface)", borderColor: "var(--p-line)", color: "var(--p-ink)",
};
const fieldClass =
  "w-full rounded-[calc(var(--p-radius)*0.55)] border px-3.5 py-2.5 text-sm outline-none " +
  "transition-colors placeholder:opacity-50 focus:border-[color:var(--p-accent)]";

function Input(props: InputHTMLAttributes<HTMLInputElement>) {
  return <input {...props} className={fieldClass} style={fieldStyle} />;
}
function Textarea(props: React.TextareaHTMLAttributes<HTMLTextAreaElement>) {
  return <textarea rows={2} {...props} className={cn(fieldClass, "resize-none")} style={fieldStyle} />;
}
