import { useMemo, useState } from "react";
import { CalendarClock, MoreHorizontal, Plus } from "lucide-react";
import { toast } from "sonner";
import { PageHeader } from "@/components/shared/PageHeader";
import { SectionCard } from "@/components/shared/SectionCard";
import { EmptyState } from "@/components/shared/EmptyState";
import { ErrorState } from "@/components/shared/ErrorState";
import { StatusBadge } from "@/components/shared/StatusBadge";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Skeleton } from "@/components/ui/skeleton";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { ApiError } from "@/services/api-client";
import { useCustomers } from "@/modules/customers/hooks/use-customers";
import type { SvcAppointmentDto, SvcAppointmentStatus } from "../api/service.api";
import { useAppointments, useChangeAppointmentStatus } from "../hooks/useAppointments";
import { useProfessionals } from "../hooks/useProfessionals";
import { useCatalog } from "../hooks/useCatalog";
import { useServicePreset } from "../context/ServicePresetContext";
import {
  allowedTransitions,
  transitionNeedsReason,
  APPOINTMENT_STATUS_LABELS,
  APPOINTMENT_STATUS_VARIANTS,
  isTerminalStatus,
} from "../lib/appointment-status";
import { AppointmentDialog } from "../components/AppointmentDialog";
import { AppointmentStatusDialog } from "../components/AppointmentStatusDialog";

const ACTION_LABELS: Record<SvcAppointmentStatus, string> = {
  Scheduled: "Reagendar",
  Confirmed: "Confirmar",
  InProgress: "Iniciar atendimento",
  Completed: "Concluir",
  NoShow: "Não compareceu",
  Cancelled: "Cancelar",
};

function todayInput() {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
}
function hhmm(iso: string) {
  return new Date(iso).toLocaleTimeString("pt-BR", { hour: "2-digit", minute: "2-digit" });
}

export default function AgendaPage() {
  const { labels } = useServicePreset();
  const apptTerm = labels?.appointment ?? "Agendamento";

  const [date, setDate] = useState(todayInput());
  const [professionalFilter, setProfessionalFilter] = useState("all");
  const [dialog, setDialog] = useState<{ open: boolean; editing: SvcAppointmentDto | null }>({
    open: false,
    editing: null,
  });
  const [statusDialog, setStatusDialog] = useState<{
    appt: SvcAppointmentDto | null;
    target: SvcAppointmentStatus | null;
  }>({ appt: null, target: null });

  const range = useMemo(() => ({
    from: new Date(`${date}T00:00:00`).toISOString(),
    to: new Date(`${date}T23:59:59.999`).toISOString(),
  }), [date]);

  const { data, isLoading, isError, refetch } = useAppointments({
    from: range.from,
    to: range.to,
    professionalId: professionalFilter === "all" ? undefined : professionalFilter,
  });
  const { data: customers } = useCustomers(false);
  const { data: professionals } = useProfessionals(false);
  const { data: catalog } = useCatalog(false);
  const change = useChangeAppointmentStatus();

  const nameOf = (list: { id: string; name: string }[] | undefined, id: string) =>
    list?.find((x) => x.id === id)?.name ?? "—";
  const customerName = (id: string) => nameOf(customers, id);
  const professionalName = (id: string) => nameOf(professionals, id);
  const serviceName = (id: string) => catalog?.find((c) => c.id === id)?.name ?? "—";

  const appointments = useMemo(
    () => [...(data ?? [])].sort((a, b) => a.startsAt.localeCompare(b.startsAt)),
    [data]
  );

  const handleTransition = (appt: SvcAppointmentDto, target: SvcAppointmentStatus) => {
    if (transitionNeedsReason(target)) {
      setStatusDialog({ appt, target });
      return;
    }
    change.mutate(
      { id: appt.id, status: target },
      {
        onSuccess: () => toast.success(`Status: ${APPOINTMENT_STATUS_LABELS[target]}.`),
        onError: (e) =>
          toast.error(e instanceof ApiError ? e.message : "Não foi possível alterar o status."),
      }
    );
  };

  return (
    <div className="space-y-6">
      <PageHeader
        eyebrow="Agenda"
        title={`${apptTerm}s`}
        description="Agenda do dia por profissional."
        actions={
          <Button onClick={() => setDialog({ open: true, editing: null })}>
            <Plus className="mr-2 h-4 w-4" /> Novo {apptTerm.toLowerCase()}
          </Button>
        }
      />

      <SectionCard noPadding>
        <div className="flex flex-wrap items-center gap-3 px-5 py-3">
          <Input
            type="date"
            value={date}
            onChange={(e) => setDate(e.target.value)}
            className="h-9 w-40"
          />
          <Select value={professionalFilter} onValueChange={setProfessionalFilter}>
            <SelectTrigger className="h-9 w-52"><SelectValue /></SelectTrigger>
            <SelectContent>
              <SelectItem value="all">Todos os profissionais</SelectItem>
              {(professionals ?? []).map((p) => (
                <SelectItem key={p.id} value={p.id}>{p.name}</SelectItem>
              ))}
            </SelectContent>
          </Select>
          <span className="ml-auto text-[12px] text-muted-foreground">
            {appointments.length} {appointments.length === 1 ? "agendamento" : "agendamentos"}
          </span>
        </div>

        {isLoading && (
          <div className="space-y-2 p-5 pt-0">
            {[1, 2, 3].map((i) => <Skeleton key={i} className="h-16 w-full" />)}
          </div>
        )}

        {isError && <ErrorState onRetry={refetch} />}

        {!isLoading && !isError && appointments.length === 0 && (
          <EmptyState
            icon={CalendarClock}
            title="Nenhum agendamento neste dia"
            description="Crie um agendamento ou escolha outra data."
            action={
              <Button variant="outline" onClick={() => setDialog({ open: true, editing: null })}>
                <Plus className="mr-2 h-4 w-4" /> Novo {apptTerm.toLowerCase()}
              </Button>
            }
          />
        )}

        {!isLoading && !isError && appointments.length > 0 && (
          <div className="divide-y divide-border border-t border-border">
            {appointments.map((appt) => {
              const transitions = allowedTransitions(appt.status);
              const terminal = isTerminalStatus(appt.status);
              return (
                <div key={appt.id} className="flex items-center gap-4 px-5 py-3">
                  <div className="w-24 shrink-0">
                    <p className="text-[13px] font-semibold text-foreground">{hhmm(appt.startsAt)}</p>
                    <p className="text-[11px] text-muted-foreground">até {hhmm(appt.endsAt)}</p>
                  </div>
                  <div className="min-w-0 flex-1">
                    <p className="truncate text-[13px] font-medium text-foreground">
                      {customerName(appt.customerId)}
                    </p>
                    <p className="truncate text-[12px] text-muted-foreground">
                      {serviceName(appt.catalogItemId)} · {professionalName(appt.professionalId)}
                    </p>
                  </div>
                  <StatusBadge
                    variant={APPOINTMENT_STATUS_VARIANTS[appt.status]}
                    label={APPOINTMENT_STATUS_LABELS[appt.status]}
                    dot
                  />
                  <DropdownMenu>
                    <DropdownMenuTrigger asChild>
                      <Button variant="ghost" size="icon" className="h-8 w-8 shrink-0">
                        <MoreHorizontal className="h-4 w-4" />
                      </Button>
                    </DropdownMenuTrigger>
                    <DropdownMenuContent align="end">
                      {!terminal && (
                        <DropdownMenuItem onClick={() => setDialog({ open: true, editing: appt })}>
                          Reagendar / editar
                        </DropdownMenuItem>
                      )}
                      {transitions.length > 0 && !terminal && <DropdownMenuSeparator />}
                      {transitions.map((t) => (
                        <DropdownMenuItem key={t} onClick={() => handleTransition(appt, t)}>
                          {ACTION_LABELS[t]}
                        </DropdownMenuItem>
                      ))}
                      {terminal && (
                        <DropdownMenuItem disabled>Sem ações disponíveis</DropdownMenuItem>
                      )}
                    </DropdownMenuContent>
                  </DropdownMenu>
                </div>
              );
            })}
          </div>
        )}
      </SectionCard>

      <AppointmentDialog
        open={dialog.open}
        appointment={dialog.editing}
        defaultDate={date}
        onClose={() => setDialog({ open: false, editing: null })}
      />
      <AppointmentStatusDialog
        open={!!statusDialog.target}
        appointment={statusDialog.appt}
        target={statusDialog.target}
        onClose={() => setStatusDialog({ appt: null, target: null })}
      />
    </div>
  );
}
