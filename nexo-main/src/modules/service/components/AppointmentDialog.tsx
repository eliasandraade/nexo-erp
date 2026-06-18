import { useEffect, useMemo, useState } from "react";
import { toast } from "sonner";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogFooter,
} from "@/components/ui/dialog";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { ApiError } from "@/services/api-client";
import { useCustomers } from "@/modules/customers/hooks/use-customers";
import type { SaveAppointmentRequest, SvcAppointmentDto } from "../api/service.api";
import { useProfessionals } from "../hooks/useProfessionals";
import { useCatalog } from "../hooks/useCatalog";
import { useSubjects } from "../hooks/useSubjects";
import { useCreateAppointment, useUpdateAppointment } from "../hooks/useAppointments";
import { useServicePreset } from "../context/ServicePresetContext";

interface AppointmentDialogProps {
  open: boolean;
  onClose: () => void;
  appointment?: SvcAppointmentDto | null;
  /** Pre-fills the date when creating from a specific agenda day (YYYY-MM-DD, local). */
  defaultDate?: string;
}

function pad(n: number) { return String(n).padStart(2, "0"); }
function toDateInput(d: Date) { return `${d.getFullYear()}-${pad(d.getMonth() + 1)}-${pad(d.getDate())}`; }
function toTimeInput(d: Date) { return `${pad(d.getHours())}:${pad(d.getMinutes())}`; }

export function AppointmentDialog({ open, onClose, appointment, defaultDate }: AppointmentDialogProps) {
  const { labels } = useServicePreset();
  const apptTerm = labels?.appointment ?? "Agendamento";
  const isEdit = !!appointment;

  const { data: customers } = useCustomers(false);
  const { data: professionals } = useProfessionals(true);
  const { data: catalog } = useCatalog(true);

  const create = useCreateAppointment();
  const update = useUpdateAppointment();
  const isPending = create.isPending || update.isPending;

  const [customerId, setCustomerId] = useState("");
  const [professionalId, setProfessionalId] = useState("");
  const [catalogItemId, setCatalogItemId] = useState("");
  const [subjectId, setSubjectId] = useState("");
  const [date, setDate] = useState("");
  const [time, setTime] = useState("09:00");
  const [duration, setDuration] = useState(30);
  const [notes, setNotes] = useState("");

  const selectedCatalog = useMemo(
    () => catalog?.find((c) => c.id === catalogItemId),
    [catalog, catalogItemId]
  );
  const requiresSubject = !!selectedCatalog?.requiresSubject;

  const { data: subjects } = useSubjects({ customerId: customerId || undefined, active: true });

  useEffect(() => {
    if (!open) return;
    if (appointment) {
      const start = new Date(appointment.startsAt);
      const end = new Date(appointment.endsAt);
      setCustomerId(appointment.customerId);
      setProfessionalId(appointment.professionalId);
      setCatalogItemId(appointment.catalogItemId);
      setSubjectId(appointment.subjectId ?? "");
      setDate(toDateInput(start));
      setTime(toTimeInput(start));
      setDuration(Math.max(5, Math.round((end.getTime() - start.getTime()) / 60000)));
      setNotes(appointment.notes ?? "");
    } else {
      setCustomerId("");
      setProfessionalId("");
      setCatalogItemId("");
      setSubjectId("");
      setDate(defaultDate ?? toDateInput(new Date()));
      setTime("09:00");
      setDuration(30);
      setNotes("");
    }
  }, [open, appointment, defaultDate]);

  // When the service changes, adopt its default duration.
  useEffect(() => {
    if (selectedCatalog && !appointment) setDuration(selectedCatalog.durationMinutes);
  }, [selectedCatalog, appointment]);

  const handleSave = async () => {
    if (!customerId) { toast.error("Selecione o cliente."); return; }
    if (!professionalId) { toast.error("Selecione o profissional."); return; }
    if (!catalogItemId) { toast.error("Selecione o serviço."); return; }
    if (!date || !time) { toast.error("Informe data e horário."); return; }
    if (requiresSubject && !subjectId) {
      toast.error(`Este serviço exige um ${labels?.subject?.toLowerCase() ?? "cadastro"}.`);
      return;
    }

    const start = new Date(`${date}T${time}`);
    if (Number.isNaN(start.getTime())) { toast.error("Data/horário inválidos."); return; }
    const end = new Date(start.getTime() + duration * 60000);

    const body: SaveAppointmentRequest = {
      customerId,
      professionalId,
      catalogItemId,
      startsAt: start.toISOString(),
      endsAt: end.toISOString(),
      subjectId: requiresSubject ? subjectId : subjectId || null,
      notes: notes.trim() || null,
    };

    try {
      if (isEdit) {
        await update.mutateAsync({ id: appointment.id, body });
        toast.success(`${apptTerm} remarcado.`);
      } else {
        await create.mutateAsync(body);
        toast.success(`${apptTerm} criado.`);
      }
      onClose();
    } catch (e) {
      if (e instanceof ApiError && e.status === 409) {
        toast.error("Esse profissional já tem um agendamento nesse horário.");
      } else if (e instanceof ApiError) {
        toast.error(e.message || `Não foi possível salvar o ${apptTerm.toLowerCase()}.`);
      } else {
        toast.error(`Não foi possível salvar o ${apptTerm.toLowerCase()}.`);
      }
    }
  };

  return (
    <Dialog open={open} onOpenChange={(v) => { if (!v && !isPending) onClose(); }}>
      <DialogContent className="max-w-lg">
        <DialogHeader>
          <DialogTitle>{isEdit ? `Remarcar ${apptTerm.toLowerCase()}` : `Novo ${apptTerm.toLowerCase()}`}</DialogTitle>
        </DialogHeader>

        <div className="space-y-3 py-1">
          <div className="space-y-1.5">
            <Label>{labels?.customer ?? "Cliente"} *</Label>
            <Select value={customerId} onValueChange={(v) => { setCustomerId(v); setSubjectId(""); }} disabled={isPending}>
              <SelectTrigger><SelectValue placeholder="Selecione" /></SelectTrigger>
              <SelectContent>
                {(customers ?? []).map((c) => <SelectItem key={c.id} value={c.id}>{c.name}</SelectItem>)}
              </SelectContent>
            </Select>
          </div>

          <div className="grid grid-cols-2 gap-3">
            <div className="space-y-1.5">
              <Label>{labels?.professional ?? "Profissional"} *</Label>
              <Select value={professionalId} onValueChange={setProfessionalId} disabled={isPending}>
                <SelectTrigger><SelectValue placeholder="Selecione" /></SelectTrigger>
                <SelectContent>
                  {(professionals ?? []).map((p) => <SelectItem key={p.id} value={p.id}>{p.name}</SelectItem>)}
                </SelectContent>
              </Select>
            </div>
            <div className="space-y-1.5">
              <Label>{labels?.catalogItem ?? "Serviço"} *</Label>
              <Select value={catalogItemId} onValueChange={setCatalogItemId} disabled={isPending}>
                <SelectTrigger><SelectValue placeholder="Selecione" /></SelectTrigger>
                <SelectContent>
                  {(catalog ?? []).map((c) => <SelectItem key={c.id} value={c.id}>{c.name}</SelectItem>)}
                </SelectContent>
              </Select>
            </div>
          </div>

          {requiresSubject && (
            <div className="space-y-1.5">
              <Label>{labels?.subject ?? "Cadastro"} *</Label>
              <Select value={subjectId} onValueChange={setSubjectId} disabled={isPending || !customerId}>
                <SelectTrigger>
                  <SelectValue placeholder={customerId ? "Selecione" : "Escolha o cliente primeiro"} />
                </SelectTrigger>
                <SelectContent>
                  {(subjects ?? []).map((s) => <SelectItem key={s.id} value={s.id}>{s.displayName}</SelectItem>)}
                </SelectContent>
              </Select>
            </div>
          )}

          <div className="grid grid-cols-3 gap-3">
            <div className="space-y-1.5">
              <Label htmlFor="appt-date">Data *</Label>
              <Input id="appt-date" type="date" value={date} onChange={(e) => setDate(e.target.value)} disabled={isPending} />
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="appt-time">Hora *</Label>
              <Input id="appt-time" type="time" value={time} onChange={(e) => setTime(e.target.value)} disabled={isPending} />
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="appt-duration">Duração (min)</Label>
              <Input id="appt-duration" type="number" min={5} step={5} value={duration}
                onChange={(e) => setDuration(Number(e.target.value))} disabled={isPending} />
            </div>
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="appt-notes">Observações</Label>
            <Textarea id="appt-notes" value={notes} rows={2} onChange={(e) => setNotes(e.target.value)}
              maxLength={2000} disabled={isPending} />
          </div>
        </div>

        <DialogFooter className="gap-2">
          <Button variant="outline" onClick={onClose} disabled={isPending}>Cancelar</Button>
          <Button onClick={handleSave} disabled={isPending}>{isPending ? "Salvando..." : "Salvar"}</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
