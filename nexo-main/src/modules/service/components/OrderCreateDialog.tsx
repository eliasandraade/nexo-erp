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
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { useCustomers } from "@/modules/customers/hooks/use-customers";
import { useProfessionals } from "../hooks/useProfessionals";
import { useSubjects } from "../hooks/useSubjects";
import { useCreateOrder } from "../hooks/useOrders";
import { useServicePreset } from "../context/ServicePresetContext";

interface OrderCreateDialogProps {
  open: boolean;
  onClose: () => void;
}

export function OrderCreateDialog({ open, onClose }: OrderCreateDialogProps) {
  const { labels, capabilities } = useServicePreset();
  const orderTerm = labels?.order ?? "Ordem";
  const navigate = useNavigate();

  const { data: customers } = useCustomers(false);
  const { data: professionals } = useProfessionals(true);
  const create = useCreateOrder();

  const [customerId, setCustomerId] = useState("");
  const [subjectId, setSubjectId] = useState("");
  const [professionalId, setProfessionalId] = useState("");
  const [notes, setNotes] = useState("");

  const usesSubjects = capabilities?.subjectKind != null;
  const { data: subjects } = useSubjects({ customerId: customerId || undefined, active: true });

  useEffect(() => {
    if (!open) return;
    setCustomerId(""); setSubjectId(""); setProfessionalId(""); setNotes("");
  }, [open]);

  const handleCreate = async () => {
    if (!customerId) { toast.error("Selecione o cliente."); return; }
    try {
      const order = await create.mutateAsync({
        customerId,
        subjectId: usesSubjects ? subjectId || null : null,
        professionalId: professionalId || null,
        notes: notes.trim() || null,
      });
      toast.success(`${orderTerm} criada.`);
      onClose();
      navigate(`/service/ordens/${order.id}`);
    } catch {
      toast.error(`Não foi possível criar a ${orderTerm.toLowerCase()}.`);
    }
  };

  return (
    <Dialog open={open} onOpenChange={(v) => { if (!v && !create.isPending) onClose(); }}>
      <DialogContent className="max-w-lg">
        <DialogHeader>
          <DialogTitle>Nova {orderTerm.toLowerCase()}</DialogTitle>
        </DialogHeader>

        <div className="space-y-3 py-1">
          <div className="space-y-1.5">
            <Label>{labels?.customer ?? "Cliente"} *</Label>
            <Select value={customerId} onValueChange={(v) => { setCustomerId(v); setSubjectId(""); }} disabled={create.isPending}>
              <SelectTrigger><SelectValue placeholder="Selecione" /></SelectTrigger>
              <SelectContent>
                {(customers ?? []).map((c) => <SelectItem key={c.id} value={c.id}>{c.name}</SelectItem>)}
              </SelectContent>
            </Select>
          </div>

          <div className="grid grid-cols-2 gap-3">
            {usesSubjects && (
              <div className="space-y-1.5">
                <Label>{labels?.subject ?? "Cadastro"}</Label>
                <Select value={subjectId} onValueChange={setSubjectId} disabled={create.isPending || !customerId}>
                  <SelectTrigger><SelectValue placeholder="Opcional" /></SelectTrigger>
                  <SelectContent>
                    {(subjects ?? []).map((s) => <SelectItem key={s.id} value={s.id}>{s.displayName}</SelectItem>)}
                  </SelectContent>
                </Select>
              </div>
            )}
            <div className="space-y-1.5">
              <Label>{labels?.professional ?? "Profissional"}</Label>
              <Select value={professionalId} onValueChange={setProfessionalId} disabled={create.isPending}>
                <SelectTrigger><SelectValue placeholder="Opcional" /></SelectTrigger>
                <SelectContent>
                  {(professionals ?? []).map((p) => <SelectItem key={p.id} value={p.id}>{p.name}</SelectItem>)}
                </SelectContent>
              </Select>
            </div>
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="order-notes">Observações</Label>
            <Textarea id="order-notes" value={notes} rows={2} onChange={(e) => setNotes(e.target.value)}
              maxLength={2000} disabled={create.isPending} />
          </div>
        </div>

        <DialogFooter className="gap-2">
          <Button variant="outline" onClick={onClose} disabled={create.isPending}>Cancelar</Button>
          <Button onClick={handleCreate} disabled={create.isPending}>
            {create.isPending ? "Criando..." : "Criar"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
