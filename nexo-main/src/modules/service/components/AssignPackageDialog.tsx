import { useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
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
import { formatCurrency } from "@/lib/formatters";
import { ApiError } from "@/services/api-client";
import { useCustomers } from "@/modules/customers/hooks/use-customers";
import { usePackages } from "../hooks/usePackages";
import { useAssignCustomerPackage } from "../hooks/useCustomerPackages";
import { useServicePreset } from "../context/ServicePresetContext";

interface AssignPackageDialogProps {
  open: boolean;
  onClose: () => void;
}

function todayInput() {
  const d = new Date();
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, "0")}-${String(d.getDate()).padStart(2, "0")}`;
}

export function AssignPackageDialog({ open, onClose }: AssignPackageDialogProps) {
  const { labels } = useServicePreset();
  const navigate = useNavigate();
  const { data: packages } = usePackages(true);
  const { data: customers } = useCustomers(false);
  const assign = useAssignCustomerPackage();

  const [packageId, setPackageId] = useState("");
  const [customerId, setCustomerId] = useState("");
  const [startDate, setStartDate] = useState(todayInput());
  const [notes, setNotes] = useState("");

  useEffect(() => {
    if (!open) return;
    setPackageId(""); setCustomerId(""); setStartDate(todayInput()); setNotes("");
  }, [open]);

  const handleAssign = async () => {
    if (!packageId) { toast.error("Selecione o pacote."); return; }
    if (!customerId) { toast.error("Selecione o cliente."); return; }
    const startsAt = new Date(`${startDate}T12:00`).toISOString();
    try {
      const cp = await assign.mutateAsync({ packageId, customerId, startsAt, notes: notes.trim() || null });
      toast.success("Pacote atribuído ao cliente.");
      onClose();
      navigate(`/service/customer-packages/${cp.id}`);
    } catch (e) {
      toast.error(e instanceof ApiError ? e.message : "Não foi possível atribuir o pacote.");
    }
  };

  return (
    <Dialog open={open} onOpenChange={(v) => { if (!v && !assign.isPending) onClose(); }}>
      <DialogContent className="max-w-lg">
        <DialogHeader>
          <DialogTitle>Vender / atribuir pacote</DialogTitle>
        </DialogHeader>

        <div className="space-y-3 py-1">
          <div className="space-y-1.5">
            <Label>Pacote *</Label>
            <Select value={packageId} onValueChange={setPackageId} disabled={assign.isPending}>
              <SelectTrigger><SelectValue placeholder="Selecione o pacote" /></SelectTrigger>
              <SelectContent>
                {(packages ?? []).map((p) => (
                  <SelectItem key={p.id} value={p.id}>{p.name} · {formatCurrency(p.price)}</SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>

          <div className="grid grid-cols-2 gap-3">
            <div className="space-y-1.5">
              <Label>{labels?.customer ?? "Cliente"} *</Label>
              <Select value={customerId} onValueChange={setCustomerId} disabled={assign.isPending}>
                <SelectTrigger><SelectValue placeholder="Selecione" /></SelectTrigger>
                <SelectContent>
                  {(customers ?? []).map((c) => <SelectItem key={c.id} value={c.id}>{c.name}</SelectItem>)}
                </SelectContent>
              </Select>
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="assign-date">Início *</Label>
              <Input id="assign-date" type="date" value={startDate} onChange={(e) => setStartDate(e.target.value)} disabled={assign.isPending} />
            </div>
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="assign-notes">Observações</Label>
            <Textarea id="assign-notes" value={notes} rows={2} onChange={(e) => setNotes(e.target.value)} maxLength={2000} disabled={assign.isPending} />
          </div>
        </div>

        <DialogFooter className="gap-2">
          <Button variant="outline" onClick={onClose} disabled={assign.isPending}>Cancelar</Button>
          <Button onClick={handleAssign} disabled={assign.isPending}>{assign.isPending ? "Atribuindo..." : "Atribuir"}</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
