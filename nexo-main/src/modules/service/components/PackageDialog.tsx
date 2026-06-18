import { useEffect, useState } from "react";
import { Trash2 } from "lucide-react";
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
import type { SvcPackageDto } from "../api/service.api";
import { useCatalog } from "../hooks/useCatalog";
import {
  useCreatePackage,
  useUpdatePackage,
  useUpdatePackagePrice,
  useAddPackageItem,
  useRemovePackageItem,
} from "../hooks/usePackages";

interface PackageDialogProps {
  open: boolean;
  onClose: () => void;
  pkg?: SvcPackageDto | null;
}

export function PackageDialog({ open, onClose, pkg }: PackageDialogProps) {
  const isEdit = !!pkg;
  const { data: catalog } = useCatalog(true);

  const create = useCreatePackage();
  const update = useUpdatePackage();
  const updatePrice = useUpdatePackagePrice();
  const addItem = useAddPackageItem();
  const removeItem = useRemovePackageItem();
  const isPending = create.isPending || update.isPending || updatePrice.isPending;

  const [name, setName] = useState("");
  const [price, setPrice] = useState("");
  const [validityDays, setValidityDays] = useState("");
  const [description, setDescription] = useState("");
  const [newItemId, setNewItemId] = useState("");
  const [newQty, setNewQty] = useState("1");

  useEffect(() => {
    if (!open) return;
    setName(pkg?.name ?? "");
    setPrice(pkg?.price?.toString() ?? "");
    setValidityDays(pkg?.validityDays?.toString() ?? "");
    setDescription(pkg?.description ?? "");
    setNewItemId(""); setNewQty("1");
  }, [open, pkg]);

  const handleSave = async () => {
    if (!name.trim()) { toast.error("Nome é obrigatório."); return; }
    const priceValue = Number(price);
    if (!isEdit && (!Number.isFinite(priceValue) || priceValue < 0)) { toast.error("Preço inválido."); return; }
    const validity = validityDays.trim() ? Number(validityDays) : null;
    if (validity !== null && (!Number.isInteger(validity) || validity <= 0)) {
      toast.error("Validade deve ser um número de dias positivo."); return;
    }

    try {
      if (isEdit) {
        await update.mutateAsync({ id: pkg.id, body: { name: name.trim(), description: description.trim() || null, validityDays: validity } });
        if (Number.isFinite(priceValue) && priceValue !== pkg.price) {
          await updatePrice.mutateAsync({ id: pkg.id, price: priceValue });
        }
        toast.success("Pacote atualizado.");
        onClose();
      } else {
        await create.mutateAsync({ name: name.trim(), price: priceValue, description: description.trim() || null, validityDays: validity });
        toast.success("Pacote criado. Adicione os itens incluídos editando-o.");
        onClose();
      }
    } catch {
      toast.error("Não foi possível salvar o pacote.");
    }
  };

  const handleAddItem = async () => {
    if (!pkg) return;
    if (!newItemId) { toast.error("Escolha um serviço."); return; }
    const qty = Number(newQty);
    if (!Number.isFinite(qty) || qty <= 0) { toast.error("Quantidade inválida."); return; }
    try {
      await addItem.mutateAsync({ id: pkg.id, body: { catalogItemId: newItemId, includedQuantity: qty } });
      setNewItemId(""); setNewQty("1");
    } catch {
      toast.error("Não foi possível adicionar o item.");
    }
  };

  return (
    <Dialog open={open} onOpenChange={(v) => { if (!v && !isPending) onClose(); }}>
      <DialogContent className="max-w-lg">
        <DialogHeader>
          <DialogTitle>{isEdit ? "Editar pacote" : "Novo pacote"}</DialogTitle>
        </DialogHeader>

        <div className="space-y-3 py-1">
          <div className="space-y-1.5">
            <Label htmlFor="pkg-name">Nome *</Label>
            <Input id="pkg-name" value={name} onChange={(e) => setName(e.target.value)} maxLength={200} disabled={isPending} autoFocus />
          </div>
          <div className="grid grid-cols-2 gap-3">
            <div className="space-y-1.5">
              <Label htmlFor="pkg-price">Preço (R$) *</Label>
              <Input id="pkg-price" type="number" min={0} step="0.01" value={price} onChange={(e) => setPrice(e.target.value)} disabled={isPending} />
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="pkg-validity">Validade (dias)</Label>
              <Input id="pkg-validity" type="number" min={1} value={validityDays} onChange={(e) => setValidityDays(e.target.value)}
                placeholder="Sem validade" disabled={isPending} />
            </div>
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="pkg-desc">Descrição</Label>
            <Textarea id="pkg-desc" value={description} rows={2} onChange={(e) => setDescription(e.target.value)} maxLength={1000} disabled={isPending} />
          </div>

          {isEdit && (
            <div className="space-y-2 rounded-md border border-border p-3">
              <p className="text-[12px] font-semibold text-foreground">Serviços incluídos</p>
              {pkg.items.length === 0 ? (
                <p className="text-[12px] text-muted-foreground">Nenhum serviço incluído ainda.</p>
              ) : (
                <div className="space-y-1">
                  {pkg.items.map((it) => (
                    <div key={it.id} className="flex items-center justify-between text-[12.5px]">
                      <span className="text-foreground">{it.nameSnapshot}</span>
                      <span className="flex items-center gap-2 text-muted-foreground">
                        {it.includedQuantity}x
                        <button onClick={() => removeItem.mutate({ id: pkg.id, itemId: it.id })} disabled={removeItem.isPending}
                          className="hover:text-destructive" aria-label="Remover">
                          <Trash2 className="h-3.5 w-3.5" />
                        </button>
                      </span>
                    </div>
                  ))}
                </div>
              )}
              <div className="flex items-end gap-2 pt-1">
                <div className="flex-1">
                  <Select value={newItemId} onValueChange={setNewItemId} disabled={addItem.isPending}>
                    <SelectTrigger className="h-8 text-[12px]"><SelectValue placeholder="Adicionar serviço" /></SelectTrigger>
                    <SelectContent>
                      {(catalog ?? []).map((c) => (
                        <SelectItem key={c.id} value={c.id}>{c.name} · {formatCurrency(c.price)}</SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                </div>
                <Input type="number" min={1} value={newQty} onChange={(e) => setNewQty(e.target.value)} className="h-8 w-16" disabled={addItem.isPending} />
                <Button size="sm" variant="outline" onClick={handleAddItem} disabled={addItem.isPending || !newItemId}>Incluir</Button>
              </div>
            </div>
          )}
        </div>

        <DialogFooter className="gap-2">
          <Button variant="outline" onClick={onClose} disabled={isPending}>Fechar</Button>
          <Button onClick={handleSave} disabled={isPending}>{isPending ? "Salvando..." : "Salvar"}</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
