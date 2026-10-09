import { useEffect, useState } from "react";
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
import type { SvcCustomerPackageDto } from "../api/service.api";
import { useConsumeCustomerPackage } from "../hooks/useCustomerPackages";
import { useProfessionals } from "../hooks/useProfessionals";
import { useAppointments } from "../hooks/useAppointments";
import { formatDateTime } from "@/lib/formatters";
import { useServicePreset } from "../context/ServicePresetContext";

/** Radix Select cannot hold an empty value — sentinel for "none selected". */
const NO_PROFESSIONAL = "none";
const NO_APPOINTMENT = "none";

interface ConsumePackageDialogProps {
  open: boolean;
  onClose: () => void;
  customerPackage: SvcCustomerPackageDto | null;
}

export function ConsumePackageDialog({ open, onClose, customerPackage }: ConsumePackageDialogProps) {
  const consume = useConsumeCustomerPackage();
  const { labels, capabilities } = useServicePreset();
  const professionalTerm = labels?.professional ?? "Profissional";
  const { data: professionals } = useProfessionals(true);
  const [catalogItemId, setCatalogItemId] = useState("");
  const [quantity, setQuantity] = useState("1");
  const [professionalId, setProfessionalId] = useState(NO_PROFESSIONAL);
  const [appointmentId, setAppointmentId] = useState(NO_APPOINTMENT);
  const appointmentTerm = labels?.appointment ?? "Agendamento";
  const { data: customerAppointments } = useAppointments(
    customerPackage ? { customerId: customerPackage.customerId } : {},
  );
  // Appointments this consumption can pay for: same customer, same service, not cancelled/no-show.
  const linkableAppointments = (open && customerPackage ? customerAppointments ?? [] : [])
    .filter((a) => a.catalogItemId === catalogItemId && a.status !== "Cancelled" && a.status !== "NoShow")
    .sort((a, b) => b.startsAt.localeCompare(a.startsAt))
    .slice(0, 20);
  const [notes, setNotes] = useState("");

  const available = (customerPackage?.items ?? []).filter((it) => it.remainingQuantity > 0);

  useEffect(() => {
    if (!open) return;
    setCatalogItemId(available[0]?.catalogItemId ?? "");
    setQuantity("1");
    setProfessionalId(NO_PROFESSIONAL);
    setAppointmentId(NO_APPOINTMENT);
    setNotes("");
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, customerPackage]);

  const handleConsume = async () => {
    if (!customerPackage) return;
    if (!catalogItemId) { toast.error("Selecione um serviço com saldo."); return; }
    const qty = Number(quantity);
    if (!Number.isFinite(qty) || qty <= 0) { toast.error("Quantidade inválida."); return; }

    try {
      await consume.mutateAsync({
        id: customerPackage.id,
        body: {
          catalogItemId,
          quantity: qty,
          notes: notes.trim() || null,
          professionalId: professionalId === NO_PROFESSIONAL ? null : professionalId,
          appointmentId: appointmentId === NO_APPOINTMENT ? null : appointmentId,
        },
      });
      toast.success("Saldo consumido.");
      onClose();
    } catch (e) {
      toast.error(e instanceof ApiError ? e.message : "Não foi possível consumir o saldo.");
    }
  };

  return (
    <Dialog open={open} onOpenChange={(v) => { if (!v && !consume.isPending) onClose(); }}>
      <DialogContent className="max-w-md">
        <DialogHeader>
          <DialogTitle>Consumir saldo do pacote</DialogTitle>
        </DialogHeader>

        <div className="space-y-3 py-1">
          {available.length === 0 ? (
            <p className="py-4 text-center text-[12.5px] text-muted-foreground">
              Não há saldo disponível neste pacote.
            </p>
          ) : (
            <>
              <div className="space-y-1.5">
                <Label>Serviço *</Label>
                <Select
                  value={catalogItemId}
                  onValueChange={(v) => { setCatalogItemId(v); setAppointmentId(NO_APPOINTMENT); }}
                  disabled={consume.isPending}
                >
                  <SelectTrigger><SelectValue placeholder="Selecione" /></SelectTrigger>
                  <SelectContent>
                    {available.map((it) => (
                      <SelectItem key={it.id} value={it.catalogItemId}>
                        {it.nameSnapshot} · saldo {it.remainingQuantity}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>
              <div className="space-y-1.5">
                <Label htmlFor="consume-qty">Quantidade *</Label>
                <Input id="consume-qty" type="number" min={1} step={1} value={quantity}
                  onChange={(e) => setQuantity(e.target.value)} disabled={consume.isPending} />
              </div>
              {linkableAppointments.length > 0 && (
                <div className="space-y-1.5">
                  <Label>{appointmentTerm} atendido</Label>
                  <Select
                    value={appointmentId}
                    onValueChange={(v) => {
                      setAppointmentId(v);
                      const appt = linkableAppointments.find((a) => a.id === v);
                      if (appt && professionalId === NO_PROFESSIONAL) setProfessionalId(appt.professionalId);
                    }}
                    disabled={consume.isPending}
                  >
                    <SelectTrigger><SelectValue /></SelectTrigger>
                    <SelectContent>
                      <SelectItem value={NO_APPOINTMENT}>Nenhum</SelectItem>
                      {linkableAppointments.map((a) => (
                        <SelectItem key={a.id} value={a.id}>{formatDateTime(a.startsAt)}</SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                  {capabilities?.commissions && (
                    <p className="text-[11.5px] text-muted-foreground">
                      Vincule o {appointmentTerm.toLowerCase()} pago com o pacote para a comissão não ser contada duas vezes.
                    </p>
                  )}
                </div>
              )}
              <div className="space-y-1.5">
                <Label>{professionalTerm}</Label>
                <Select value={professionalId} onValueChange={setProfessionalId} disabled={consume.isPending}>
                  <SelectTrigger><SelectValue /></SelectTrigger>
                  <SelectContent>
                    <SelectItem value={NO_PROFESSIONAL}>Não informar</SelectItem>
                    {(professionals ?? []).map((p) => (
                      <SelectItem key={p.id} value={p.id}>{p.name}</SelectItem>
                    ))}
                  </SelectContent>
                </Select>
                {capabilities?.commissions && (
                  <p className="text-[11.5px] text-muted-foreground">
                    Quem executou o serviço recebe a comissão deste consumo.
                  </p>
                )}
              </div>
              <div className="space-y-1.5">
                <Label htmlFor="consume-notes">Observações</Label>
                <Textarea id="consume-notes" value={notes} rows={2} onChange={(e) => setNotes(e.target.value)} maxLength={2000} disabled={consume.isPending} />
              </div>
            </>
          )}
        </div>

        <DialogFooter className="gap-2">
          <Button variant="outline" onClick={onClose} disabled={consume.isPending}>Cancelar</Button>
          <Button onClick={handleConsume} disabled={consume.isPending || available.length === 0}>
            {consume.isPending ? "Consumindo..." : "Consumir"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
