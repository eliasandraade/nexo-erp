import { CalendarCheck, CheckCircle2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import type { PublicAppointmentCreated } from "../api/booking.api";
import { formatSlotLabel } from "../lib/booking-format";

interface BookingSuccessProps {
  created:   PublicAppointmentCreated;
  onRestart: () => void;
}

export function BookingSuccess({ created, onRestart }: BookingSuccessProps) {
  const confirmed = created.status === "Confirmed";

  return (
    <div className="flex min-h-screen flex-col items-center justify-center bg-background px-6 text-center text-foreground">
      <div className="flex w-full max-w-sm flex-col items-center gap-5">
        <div className="flex h-16 w-16 items-center justify-center rounded-full bg-green-500/15 text-green-500">
          <CheckCircle2 className="h-9 w-9" />
        </div>

        <div>
          <h1 className="text-xl font-bold">Agendamento realizado!</h1>
          <p className="mt-1 text-sm text-muted-foreground">
            {confirmed
              ? "Seu horário está confirmado."
              : "Recebemos seu pedido — você receberá a confirmação em breve."}
          </p>
        </div>

        <div className="w-full rounded-xl border border-border bg-card p-4 text-left text-sm">
          <Row icon={<CalendarCheck className="h-4 w-4 text-primary" />} label="Quando">
            <span className="font-medium capitalize">{formatSlotLabel(created.startsAt)}</span>
          </Row>
          <div className="my-3 h-px bg-border" />
          <dl className="flex flex-col gap-1.5 text-muted-foreground">
            <Detail term="Serviço" value={created.serviceName} />
            <Detail term="Profissional" value={created.professionalName} />
            <Detail term="Em nome de" value={created.customerName} />
          </dl>
        </div>

        <Button variant="outline" className="w-full" onClick={onRestart}>
          Fazer outro agendamento
        </Button>
      </div>
    </div>
  );
}

function Row({ icon, label, children }: { icon: React.ReactNode; label: string; children: React.ReactNode }) {
  return (
    <div className="flex items-center gap-2">
      {icon}
      <span className="text-xs uppercase tracking-wide text-muted-foreground">{label}</span>
      <span className="ml-auto">{children}</span>
    </div>
  );
}

function Detail({ term, value }: { term: string; value: string }) {
  return (
    <div className="flex justify-between gap-3">
      <dt>{term}</dt>
      <dd className="font-medium text-foreground">{value}</dd>
    </div>
  );
}
