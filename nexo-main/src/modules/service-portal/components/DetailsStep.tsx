import { useState } from "react";
import { AlertCircle, CalendarClock, Loader2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
import type { PublicCatalogItem, PublicProfessional, ServiceLabels } from "../api/booking.api";
import { formatDuration, formatPrice, formatSlotLabel, isValidPhone } from "../lib/booking-format";

export interface DetailsFormValue {
  customerName: string;
  phone:        string;
  email:        string;
  subjectName:  string;
  subjectNotes: string;
  notes:        string;
}

interface DetailsStepProps {
  service:      PublicCatalogItem;
  professional: PublicProfessional;
  slotIso:      string;
  labels:       ServiceLabels;
  showPrices:   boolean;
  submitting:   boolean;
  errorMessage: string | null;
  onConfirm:    (value: DetailsFormValue) => void;
}

export function DetailsStep({
  service, professional, slotIso, labels, showPrices, submitting, errorMessage, onConfirm,
}: DetailsStepProps) {
  const [value, setValue] = useState<DetailsFormValue>({
    customerName: "", phone: "", email: "", subjectName: "", subjectNotes: "", notes: "",
  });
  const set = (patch: Partial<DetailsFormValue>) => setValue((v) => ({ ...v, ...patch }));

  const subjectNoun = labels.subject.toLowerCase();
  const customerNoun = labels.customer.toLowerCase();

  const phoneValid = isValidPhone(value.phone);
  const canSubmit =
    value.customerName.trim().length > 0 &&
    phoneValid &&
    (!service.requiresSubject || value.subjectName.trim().length > 0) &&
    !submitting;

  return (
    <section className="flex flex-col gap-5">
      <h2 className="text-lg font-semibold">Seus dados</h2>

      {/* Summary */}
      <div className="rounded-xl border border-border bg-card p-4">
        <div className="flex items-start gap-3">
          <CalendarClock className="mt-0.5 h-5 w-5 shrink-0 text-primary" />
          <div className="min-w-0 text-sm">
            <p className="font-semibold">{service.name}</p>
            <p className="text-muted-foreground">
              {professional.name} · {formatDuration(service.durationMinutes)}
              {showPrices && service.price !== null && <> · {formatPrice(service.price)}</>}
            </p>
            <p className="mt-1 font-medium capitalize text-foreground">{formatSlotLabel(slotIso)}</p>
          </div>
        </div>
      </div>

      {/* Customer fields */}
      <div className="flex flex-col gap-3">
        <Field label={`Nome do ${customerNoun} *`}>
          <Input
            value={value.customerName}
            onChange={(e) => set({ customerName: e.target.value })}
            placeholder="Nome completo"
            autoComplete="name"
          />
        </Field>

        <Field label="Telefone / WhatsApp *" error={value.phone.length > 0 && !phoneValid ? "Telefone inválido." : null}>
          <Input
            value={value.phone}
            onChange={(e) => set({ phone: e.target.value })}
            placeholder="(00) 00000-0000"
            type="tel"
            autoComplete="tel"
          />
        </Field>

        <Field label="E-mail (opcional)">
          <Input
            value={value.email}
            onChange={(e) => set({ email: e.target.value })}
            placeholder="voce@email.com"
            type="email"
            autoComplete="email"
          />
        </Field>

        {service.requiresSubject && (
          <>
            <Field label={`Nome do ${subjectNoun} *`}>
              <Input
                value={value.subjectName}
                onChange={(e) => set({ subjectName: e.target.value })}
                placeholder={`Identifique o ${subjectNoun}`}
              />
            </Field>
            <Field label={`Detalhes do ${subjectNoun} (opcional)`}>
              <Textarea
                value={value.subjectNotes}
                onChange={(e) => set({ subjectNotes: e.target.value })}
                placeholder="Raça, modelo, placa, observações…"
                rows={2}
                className="resize-none"
              />
            </Field>
          </>
        )}

        <Field label="Observações (opcional)">
          <Textarea
            value={value.notes}
            onChange={(e) => set({ notes: e.target.value })}
            placeholder="Algo que o profissional deva saber"
            rows={2}
            className="resize-none"
          />
        </Field>
      </div>

      {errorMessage && (
        <div className="flex items-start gap-2 rounded-lg border border-destructive/40 bg-destructive/10 px-3 py-2 text-sm text-destructive">
          <AlertCircle className="mt-0.5 h-4 w-4 shrink-0" />
          <span>{errorMessage}</span>
        </div>
      )}

      <Button
        className="h-12 w-full text-base"
        disabled={!canSubmit}
        onClick={() => onConfirm(value)}
      >
        {submitting ? <Loader2 className="h-5 w-5 animate-spin" /> : "Confirmar agendamento"}
      </Button>
    </section>
  );
}

function Field({ label, error, children }: { label: string; error?: string | null; children: React.ReactNode }) {
  return (
    <label className="flex flex-col gap-1.5">
      <span className="text-xs font-medium text-muted-foreground">{label}</span>
      {children}
      {error && <span className="text-xs text-destructive">{error}</span>}
    </label>
  );
}
