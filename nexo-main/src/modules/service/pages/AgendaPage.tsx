import { useMemo, useState } from "react";
import { useNavigate } from "react-router-dom";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { Ban, CalendarClock, MoreHorizontal, Plus, Trash2 } from "lucide-react";
import { toast } from "sonner";
import { PageHeader } from "@/components/shared/PageHeader";
import { SectionCard } from "@/components/shared/SectionCard";
import { EmptyState } from "@/components/shared/EmptyState";
import { ErrorState } from "@/components/shared/ErrorState";
import { StatusBadge } from "@/components/shared/StatusBadge";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Skeleton } from "@/components/ui/skeleton";
import { Tabs, TabsList, TabsTrigger } from "@/components/ui/tabs";
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
import {
  Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle,
} from "@/components/ui/dialog";
import { ApiError } from "@/services/api-client";
import { useCustomers } from "@/modules/customers/hooks/use-customers";
import {
  createTimeBlock, deleteTimeBlock, listTimeBlocks,
  type SvcAppointmentDto, type SvcAppointmentStatus, type SvcTimeBlockDto,
} from "../api/service.api";
import { useAppointments, useChangeAppointmentStatus } from "../hooks/useAppointments";
import { useCreateOrderFromAppointment } from "../hooks/useOrders";
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

type View = "day" | "week";

function toInput(d: Date) {
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
}
function todayInput() {
  return toInput(new Date());
}
function hhmm(iso: string) {
  return new Date(iso).toLocaleTimeString("pt-BR", { hour: "2-digit", minute: "2-digit" });
}
function dayLabel(day: string) {
  return new Date(`${day}T12:00:00`).toLocaleDateString("pt-BR", { weekday: "long", day: "2-digit", month: "2-digit" });
}
/** Local calendar days of the period: the chosen day, or its week (Monday to Sunday). */
function daysOf(date: string, view: View): string[] {
  const base = new Date(`${date}T12:00:00`);
  if (view === "day") return [date];
  const monday = new Date(base);
  monday.setDate(base.getDate() - ((base.getDay() + 6) % 7));
  return Array.from({ length: 7 }, (_, i) => {
    const d = new Date(monday);
    d.setDate(monday.getDate() + i);
    return toInput(d);
  });
}

const BLOCKS_KEY = ["service", "time-blocks"] as const;

export default function AgendaPage() {
  const { labels } = useServicePreset();
  const apptTerm = labels?.appointment ?? "Agendamento";
  const navigate = useNavigate();
  const qc = useQueryClient();

  const [date, setDate] = useState(todayInput());
  const [view, setView] = useState<View>("day");
  const [professionalFilter, setProfessionalFilter] = useState("all");
  const [dialog, setDialog] = useState<{ open: boolean; editing: SvcAppointmentDto | null }>({
    open: false,
    editing: null,
  });
  const [blocking, setBlocking] = useState(false);
  const [statusDialog, setStatusDialog] = useState<{
    appt: SvcAppointmentDto | null;
    target: SvcAppointmentStatus | null;
  }>({ appt: null, target: null });

  const days = useMemo(() => daysOf(date, view), [date, view]);
  const range = useMemo(() => ({
    from: new Date(`${days[0]}T00:00:00`).toISOString(),
    to: new Date(`${days[days.length - 1]}T23:59:59.999`).toISOString(),
  }), [days]);
  const professionalId = professionalFilter === "all" ? undefined : professionalFilter;

  const { data, isLoading, isError, refetch } = useAppointments({ from: range.from, to: range.to, professionalId });
  const { data: blocks } = useQuery({
    queryKey: [...BLOCKS_KEY, range.from, range.to, professionalId ?? "all"],
    queryFn: () => listTimeBlocks(range.from, range.to, professionalId),
  });
  const { data: customers } = useCustomers(false);
  const { data: professionals } = useProfessionals(false);
  const { data: catalog } = useCatalog(false);
  const change = useChangeAppointmentStatus();
  const openOrder = useCreateOrderFromAppointment();
  const removeBlock = useMutation({
    mutationFn: deleteTimeBlock,
    onSuccess: () => { toast.success("Bloqueio removido."); void qc.invalidateQueries({ queryKey: BLOCKS_KEY }); },
    onError: (e: Error) => toast.error(e.message || "Não foi possível remover o bloqueio."),
  });

  const nameOf = (list: { id: string; name: string }[] | undefined, id: string) =>
    list?.find((x) => x.id === id)?.name ?? "—";
  const customerName = (id: string) => nameOf(customers, id);
  const professionalName = (id: string) => nameOf(professionals, id);
  const serviceName = (id: string) => catalog?.find((c) => c.id === id)?.name ?? "—";

  const appointments = useMemo(
    () => [...(data ?? [])].sort((a, b) => a.startsAt.localeCompare(b.startsAt)),
    [data]
  );
  const onDay = (day: string, iso: string) => toInput(new Date(iso)) === day;
  const blocksOn = (day: string) => (blocks ?? []).filter((b) =>
    new Date(b.startsAt) < new Date(`${day}T23:59:59.999`) && new Date(b.endsAt) > new Date(`${day}T00:00:00`));

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

  const handleOpenOrder = (appt: SvcAppointmentDto) =>
    openOrder.mutate(appt.id, {
      onSuccess: (order) => navigate(`/service/ordens/${order.id}`),
      onError: (e) => toast.error(e instanceof ApiError ? e.message : "Não foi possível abrir a comanda."),
    });

  const renderBlock = (b: SvcTimeBlockDto) => (
    <div key={b.id} className="flex items-center gap-4 bg-muted/40 px-5 py-2.5">
      <div className="w-24 shrink-0 text-[12px] text-muted-foreground">{hhmm(b.startsAt)}–{hhmm(b.endsAt)}</div>
      <div className="min-w-0 flex-1 text-[12px]">
        <span className="inline-flex items-center gap-1 font-medium text-foreground"><Ban className="h-3.5 w-3.5" /> Bloqueado</span>
        <span className="text-muted-foreground"> · {b.professionalId ? professionalName(b.professionalId) : "Loja inteira"}{b.reason ? ` · ${b.reason}` : ""}</span>
      </div>
      <Button variant="ghost" size="icon" className="h-8 w-8" aria-label="Remover bloqueio"
        disabled={removeBlock.isPending} onClick={() => removeBlock.mutate(b.id)}>
        <Trash2 className="h-4 w-4" />
      </Button>
    </div>
  );

  const renderAppointment = (appt: SvcAppointmentDto) => {
    const transitions = allowedTransitions(appt.status);
    const terminal = isTerminalStatus(appt.status);
    const canOpenOrder = appt.status !== "Cancelled" && appt.status !== "NoShow";
    return (
      <div key={appt.id} className="flex items-center gap-4 px-5 py-3">
        <div className="w-24 shrink-0">
          <p className="text-[13px] font-semibold text-foreground">{hhmm(appt.startsAt)}</p>
          <p className="text-[11px] text-muted-foreground">até {hhmm(appt.endsAt)}</p>
        </div>
        <div className="min-w-0 flex-1">
          <p className="truncate text-[13px] font-medium text-foreground">{customerName(appt.customerId)}</p>
          <p className="truncate text-[12px] text-muted-foreground">
            {serviceName(appt.catalogItemId)} · {professionalName(appt.professionalId)}
          </p>
        </div>
        <StatusBadge variant={APPOINTMENT_STATUS_VARIANTS[appt.status]} label={APPOINTMENT_STATUS_LABELS[appt.status]} dot />
        <DropdownMenu>
          <DropdownMenuTrigger asChild>
            <Button variant="ghost" size="icon" className="h-8 w-8 shrink-0" aria-label="Ações do agendamento">
              <MoreHorizontal className="h-4 w-4" />
            </Button>
          </DropdownMenuTrigger>
          <DropdownMenuContent align="end">
            {!terminal && (
              <DropdownMenuItem onClick={() => setDialog({ open: true, editing: appt })}>
                Reagendar / editar
              </DropdownMenuItem>
            )}
            {canOpenOrder && (
              <DropdownMenuItem disabled={openOrder.isPending} onClick={() => handleOpenOrder(appt)}>
                Abrir comanda
              </DropdownMenuItem>
            )}
            {transitions.length > 0 && !terminal && <DropdownMenuSeparator />}
            {transitions.map((t) => (
              <DropdownMenuItem key={t} onClick={() => handleTransition(appt, t)}>
                {ACTION_LABELS[t]}
              </DropdownMenuItem>
            ))}
            {terminal && !canOpenOrder && (
              <DropdownMenuItem disabled>Sem ações disponíveis</DropdownMenuItem>
            )}
          </DropdownMenuContent>
        </DropdownMenu>
      </div>
    );
  };

  return (
    <div className="space-y-6">
      <PageHeader
        eyebrow="Agenda"
        title={`${apptTerm}s`}
        description="Agenda por dia ou semana, por profissional, com os horários bloqueados."
        actions={
          <div className="flex gap-2">
            <Button variant="outline" onClick={() => setBlocking(true)}>
              <Ban className="mr-2 h-4 w-4" /> Bloquear horário
            </Button>
            <Button onClick={() => setDialog({ open: true, editing: null })}>
              <Plus className="mr-2 h-4 w-4" /> Novo {apptTerm.toLowerCase()}
            </Button>
          </div>
        }
      />

      <SectionCard noPadding>
        <div className="flex flex-wrap items-center gap-3 px-5 py-3">
          <Tabs value={view} onValueChange={(v) => setView(v as View)}>
            <TabsList>
              <TabsTrigger value="day">Dia</TabsTrigger>
              <TabsTrigger value="week">Semana</TabsTrigger>
            </TabsList>
          </Tabs>
          <Input
            type="date"
            aria-label="Data"
            value={date}
            onChange={(e) => setDate(e.target.value || todayInput())}
            className="h-9 w-40"
          />
          <Select value={professionalFilter} onValueChange={setProfessionalFilter}>
            <SelectTrigger className="h-9 w-52" aria-label="Profissional"><SelectValue /></SelectTrigger>
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

        {!isLoading && !isError && appointments.length === 0 && (blocks ?? []).length === 0 && (
          <EmptyState
            icon={CalendarClock}
            title={view === "day" ? "Nenhum agendamento neste dia" : "Nenhum agendamento nesta semana"}
            description="Crie um agendamento ou escolha outra data."
            action={
              <Button variant="outline" onClick={() => setDialog({ open: true, editing: null })}>
                <Plus className="mr-2 h-4 w-4" /> Novo {apptTerm.toLowerCase()}
              </Button>
            }
          />
        )}

        {!isLoading && !isError && (appointments.length > 0 || (blocks ?? []).length > 0) && (
          <div className="border-t border-border">
            {days.map((day) => {
              const dayAppts = appointments.filter((a) => onDay(day, a.startsAt));
              const dayBlocks = blocksOn(day);
              if (view === "week" && dayAppts.length === 0 && dayBlocks.length === 0) return null;
              return (
                <div key={day}>
                  {view === "week" && (
                    <p className="bg-muted/30 px-5 py-1.5 text-[11px] font-semibold uppercase tracking-wide text-muted-foreground">
                      {dayLabel(day)}
                    </p>
                  )}
                  <div className="divide-y divide-border">
                    {dayBlocks.map(renderBlock)}
                    {dayAppts.map(renderAppointment)}
                  </div>
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
      {blocking && (
        <BlockDialog
          date={date}
          professionals={professionals ?? []}
          onClose={() => setBlocking(false)}
          onDone={() => void qc.invalidateQueries({ queryKey: BLOCKS_KEY })}
        />
      )}
    </div>
  );
}

function BlockDialog({ date, professionals, onClose, onDone }: {
  date: string;
  professionals: { id: string; name: string }[];
  onClose: () => void;
  onDone: () => void;
}) {
  const [who, setWho] = useState("store");
  const [day, setDay] = useState(date);
  const [from, setFrom] = useState("12:00");
  const [to, setTo] = useState("13:00");
  const [allDay, setAllDay] = useState(false);
  const [reason, setReason] = useState("");

  const startsAt = new Date(`${day}T${allDay ? "00:00" : from}:00`);
  const endsAt = allDay ? new Date(new Date(`${day}T00:00:00`).getTime() + 24 * 3600 * 1000) : new Date(`${day}T${to}:00`);
  const valid = !!day && endsAt > startsAt;

  const save = useMutation({
    mutationFn: () => createTimeBlock({
      professionalId: who === "store" ? null : who,
      startsAt: startsAt.toISOString(),
      endsAt: endsAt.toISOString(),
      reason: reason.trim() || null,
    }),
    onSuccess: () => { toast.success("Horário bloqueado."); onDone(); onClose(); },
    onError: (e: Error) => toast.error(e.message || "Não foi possível bloquear."),
  });

  return (
    <Dialog open onOpenChange={(v) => { if (!v && !save.isPending) onClose(); }}>
      <DialogContent className="max-w-md">
        <DialogHeader>
          <DialogTitle>Bloquear horário</DialogTitle>
          <DialogDescription>Ninguém agenda no período — nem pela equipe, nem pelo portal. O que já está agendado continua.</DialogDescription>
        </DialogHeader>
        <div className="grid gap-3 sm:grid-cols-2">
          <div className="space-y-1.5 sm:col-span-2">
            <Label htmlFor="block-who">Para</Label>
            <Select value={who} onValueChange={setWho}>
              <SelectTrigger id="block-who" aria-label="Para"><SelectValue /></SelectTrigger>
              <SelectContent>
                <SelectItem value="store">Loja inteira (feriado)</SelectItem>
                {professionals.map((p) => <SelectItem key={p.id} value={p.id}>{p.name}</SelectItem>)}
              </SelectContent>
            </Select>
          </div>
          <div className="space-y-1.5 sm:col-span-2">
            <Label htmlFor="block-day">Dia</Label>
            <Input id="block-day" type="date" value={day} onChange={(e) => setDay(e.target.value)} />
          </div>
          <label className="flex items-center gap-2 text-sm sm:col-span-2">
            <input type="checkbox" checked={allDay} onChange={(e) => setAllDay(e.target.checked)} /> O dia todo
          </label>
          {!allDay && (
            <>
              <div className="space-y-1.5">
                <Label htmlFor="block-from">De</Label>
                <Input id="block-from" type="time" value={from} onChange={(e) => setFrom(e.target.value)} />
              </div>
              <div className="space-y-1.5">
                <Label htmlFor="block-to">Até</Label>
                <Input id="block-to" type="time" value={to} onChange={(e) => setTo(e.target.value)} />
              </div>
            </>
          )}
          <div className="space-y-1.5 sm:col-span-2">
            <Label htmlFor="block-reason">Motivo</Label>
            <Input id="block-reason" value={reason} maxLength={200} onChange={(e) => setReason(e.target.value)} placeholder="Ex.: Almoço, folga, feriado" />
          </div>
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={onClose} disabled={save.isPending}>Cancelar</Button>
          <Button onClick={() => save.mutate()} disabled={!valid || save.isPending}>Bloquear</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
