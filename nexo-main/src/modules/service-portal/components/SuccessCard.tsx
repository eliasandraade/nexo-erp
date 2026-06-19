import { useState } from "react";
import { CalendarPlus, Check, CheckCircle2, Clock, Copy, RotateCcw } from "lucide-react";
import type { PublicAppointmentCreated } from "../api/booking.api";
import { formatSlotLabel } from "../lib/booking-format";
import { buildIcs, downloadIcs } from "../lib/ics";
import { Btn, Surface, Reveal, Muted } from "./PortalPrimitives";

interface SuccessCardProps {
  created:   PublicAppointmentCreated;
  storeName: string;
  onRestart: () => void;
}

export function SuccessCard({ created, storeName, onRestart }: SuccessCardProps) {
  const [copied, setCopied] = useState(false);
  const confirmed = created.status === "Confirmed";
  const when = formatSlotLabel(created.startsAt);

  function addToCalendar() {
    const ics = buildIcs({
      uid: `${created.appointmentId}@orken`,
      startUtc: created.startsAt,
      endUtc: created.endsAt,
      title: `${created.serviceName} — ${storeName}`,
      description: `Profissional: ${created.professionalName}\nEm nome de: ${created.customerName}`,
    });
    downloadIcs(`agendamento-${storeName.toLowerCase().replace(/\s+/g, "-")}`, ics);
  }

  function copyDetails() {
    const text =
      `Agendamento — ${storeName}\n` +
      `${created.serviceName}\n` +
      `Profissional: ${created.professionalName}\n` +
      `${when}\n` +
      `Em nome de: ${created.customerName}`;
    navigator.clipboard.writeText(text).then(() => {
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    });
  }

  return (
    <Reveal>
      <div className="mx-auto flex max-w-md flex-col items-center px-1 py-6 text-center">
        <span className="flex h-16 w-16 items-center justify-center rounded-full"
          style={{ background: "color-mix(in srgb, #2ea043 16%, var(--p-surface))", color: "#2ea043" }}>
          <CheckCircle2 className="h-9 w-9" />
        </span>

        <h2 className="mt-5 text-2xl font-bold" style={{ fontFamily: "var(--p-display)" }}>
          Agendamento realizado!
        </h2>
        <p className="mt-1.5 inline-flex items-center gap-1.5 text-sm" style={{ color: "var(--p-muted)" }}>
          {confirmed ? <Check className="h-4 w-4" style={{ color: "#2ea043" }} /> : <Clock className="h-4 w-4" style={{ color: "var(--p-accent)" }} />}
          {confirmed ? "Seu horário está confirmado." : "Recebemos seu pedido — aguarde a confirmação."}
        </p>

        <Surface className="mt-6 w-full p-5 text-left">
          <p className="text-xs font-semibold uppercase tracking-wide" style={{ color: "var(--p-accent)" }}>{when}</p>
          <p className="mt-2 text-base font-semibold" style={{ fontFamily: "var(--p-display)" }}>{created.serviceName}</p>
          <dl className="mt-2 space-y-1 text-sm">
            <Row term="Profissional" value={created.professionalName} />
            <Row term="Em nome de" value={created.customerName} />
          </dl>
        </Surface>

        <div className="mt-5 grid w-full grid-cols-2 gap-2.5">
          <Btn variant="soft" onClick={addToCalendar}>
            <CalendarPlus className="h-4 w-4" /> Calendário
          </Btn>
          <Btn variant="outline" onClick={copyDetails}>
            {copied ? <><Check className="h-4 w-4" /> Copiado</> : <><Copy className="h-4 w-4" /> Copiar</>}
          </Btn>
        </div>

        <button onClick={onRestart}
          className="mt-5 inline-flex items-center gap-1.5 text-[13px] font-medium transition-opacity hover:opacity-70"
          style={{ color: "var(--p-muted)" }}>
          <RotateCcw className="h-3.5 w-3.5" /> Fazer outro agendamento
        </button>
      </div>
    </Reveal>
  );
}

function Row({ term, value }: { term: string; value: string }) {
  return (
    <div className="flex justify-between gap-3">
      <dt><Muted>{term}</Muted></dt>
      <dd className="font-medium">{value}</dd>
    </div>
  );
}
