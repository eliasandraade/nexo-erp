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

interface ConsumePackageDialogProps {
  open: boolean;
  onClose: () => void;
  customerPackage: SvcCustomerPackageDto | null;
}

export function ConsumePackageDialog({ open, onClose, customerPackage }: ConsumePackageDialogProps) {
  const consume = useConsumeCustomerPackage();
  const [catalogItemId, setCatalogItemId] = useState("");
  const [quantity, setQuantity] = useState("1");
  const [notes, setNotes] = useState("");

  const available = (customerPackage?.items ?? []).filter((it) => it.remainingQuantity > 0);

  useEffect(() => {
    if (!open) return;
    setCatalogItemId(available[0]?.catalogItemId ?? "");
    setQuantity("1");
    setNotes("");
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [open, customerPackage]);

  const handleConsume = async () => {
    if (!customerPackage) return;
    if (!catalogItemId) { toast.error("Selecione um serviço com saldo."); return; }
    const qty = Number(quantity);
    if (!Number.isFinite(qty) || qty <= 0) { toast.error("Quantidade inválida."); return; }

    try {
      await consume.mutateAsync({ id: customerPackage.id, body: { catalogItemId, quantity: qty, notes: notes.trim() || null } });
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
                <Select value={catalogItemId} onValueChange={setCatalogItemId} disabled={consume.isPending}>
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
