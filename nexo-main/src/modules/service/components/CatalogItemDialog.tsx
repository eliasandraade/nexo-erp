import { useEffect, useState } from "react";
import { toast } from "sonner";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogFooter,
} from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { Switch } from "@/components/ui/switch";
import type {
  CreateCatalogItemRequest,
  SvcCatalogItemDto,
  UpdateCatalogItemRequest,
} from "../api/service.api";
import { useCreateCatalogItem, useUpdateCatalogItem } from "../hooks/useCatalog";
import { useServicePreset } from "../context/ServicePresetContext";

interface CatalogItemDialogProps {
  open: boolean;
  onClose: () => void;
  item?: SvcCatalogItemDto | null;
}

interface FormState {
  name: string;
  category: string;
  durationMinutes: string;
  price: string;
  commissionPercent: string;
  description: string;
  requiresSubject: boolean;
}

const EMPTY: FormState = {
  name: "",
  category: "",
  durationMinutes: "30",
  price: "",
  commissionPercent: "",
  description: "",
  requiresSubject: false,
};

export function CatalogItemDialog({ open, onClose, item }: CatalogItemDialogProps) {
  const { labels, capabilities } = useServicePreset();
  const term = labels?.catalogItem ?? "Serviço";
  const usesSubjects = capabilities?.subjectKind != null;
  const usesCommissions = !!capabilities?.commissions;
  const isEdit = !!item;

  const create = useCreateCatalogItem();
  const update = useUpdateCatalogItem(item?.id ?? "");
  const isPending = create.isPending || update.isPending;

  const [form, setForm] = useState<FormState>(EMPTY);

  useEffect(() => {
    if (!open) return;
    setForm(
      item
        ? {
            name: item.name,
            category: item.category ?? "",
            durationMinutes: item.durationMinutes.toString(),
            price: item.price.toString(),
            commissionPercent: item.commissionPercent?.toString() ?? "",
            description: item.description ?? "",
            requiresSubject: item.requiresSubject,
          }
        : EMPTY
    );
  }, [open, item]);

  const set = <K extends keyof FormState>(field: K, value: FormState[K]) =>
    setForm((prev) => ({ ...prev, [field]: value }));

  const validateCommon = (): string | null => {
    if (!form.name.trim()) return "Nome é obrigatório.";
    const duration = Number(form.durationMinutes);
    if (!Number.isFinite(duration) || duration <= 0) return "Duração deve ser maior que zero.";
    return null;
  };

  const handleSave = async () => {
    const err = validateCommon();
    if (err) { toast.error(err); return; }

    try {
      if (isEdit) {
        const body: UpdateCatalogItemRequest = {
          name: form.name.trim(),
          durationMinutes: Number(form.durationMinutes),
          requiresSubject: usesSubjects ? form.requiresSubject : false,
          description: form.description.trim() || null,
          category: form.category.trim() || null,
        };
        await update.mutateAsync(body);
        toast.success(`${term} atualizado.`);
      } else {
        const price = Number(form.price);
        if (!Number.isFinite(price) || price < 0) { toast.error("Preço não pode ser negativo."); return; }
        let commission: number | null = null;
        if (usesCommissions && form.commissionPercent.trim()) {
          commission = Number(form.commissionPercent);
          if (Number.isNaN(commission) || commission < 0 || commission > 100) {
            toast.error("Comissão deve estar entre 0 e 100."); return;
          }
        }
        const body: CreateCatalogItemRequest = {
          name: form.name.trim(),
          durationMinutes: Number(form.durationMinutes),
          price,
          description: form.description.trim() || null,
          category: form.category.trim() || null,
          commissionPercent: commission,
          requiresSubject: usesSubjects ? form.requiresSubject : false,
        };
        await create.mutateAsync(body);
        toast.success(`${term} cadastrado.`);
      }
      onClose();
    } catch {
      toast.error(`Não foi possível salvar o ${term.toLowerCase()}.`);
    }
  };

  return (
    <Dialog open={open} onOpenChange={(v) => { if (!v && !isPending) onClose(); }}>
      <DialogContent className="max-w-lg">
        <DialogHeader>
          <DialogTitle>{isEdit ? `Editar ${term.toLowerCase()}` : `Novo ${term.toLowerCase()}`}</DialogTitle>
        </DialogHeader>

        <div className="grid grid-cols-2 gap-3 py-1">
          <div className="col-span-2 space-y-1.5">
            <Label htmlFor="cat-name">Nome *</Label>
            <Input id="cat-name" value={form.name} onChange={(e) => set("name", e.target.value)}
              maxLength={200} disabled={isPending} autoFocus />
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="cat-category">Categoria</Label>
            <Input id="cat-category" value={form.category} onChange={(e) => set("category", e.target.value)}
              maxLength={100} disabled={isPending} />
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="cat-duration">Duração (min) *</Label>
            <Input id="cat-duration" type="number" min={1} value={form.durationMinutes}
              onChange={(e) => set("durationMinutes", e.target.value)} disabled={isPending} />
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="cat-price">Preço (R$){isEdit ? "" : " *"}</Label>
            <Input id="cat-price" type="number" min={0} step="0.01" value={form.price}
              onChange={(e) => set("price", e.target.value)} disabled={isPending || isEdit} />
            {isEdit && (
              <p className="text-[11px] text-muted-foreground">Preço é definido na criação.</p>
            )}
          </div>
          {usesCommissions && (
            <div className="space-y-1.5">
              <Label htmlFor="cat-commission">Comissão (%)</Label>
              <Input id="cat-commission" type="number" min={0} max={100} step="0.5"
                value={form.commissionPercent} onChange={(e) => set("commissionPercent", e.target.value)}
                disabled={isPending || isEdit} />
              {isEdit && (
                <p className="text-[11px] text-muted-foreground">Comissão é definida na criação.</p>
              )}
            </div>
          )}
          <div className="col-span-2 space-y-1.5">
            <Label htmlFor="cat-description">Descrição</Label>
            <Textarea id="cat-description" value={form.description} rows={2}
              onChange={(e) => set("description", e.target.value)} maxLength={1000} disabled={isPending} />
          </div>
          {usesSubjects && (
            <div className="col-span-2 flex items-center justify-between rounded-md border border-border px-3 py-2">
              <div>
                <p className="text-[13px] font-medium text-foreground">Exige {labels?.subject?.toLowerCase() ?? "cadastro"}</p>
                <p className="text-[11.5px] text-muted-foreground">Ao agendar/lançar, escolher um {labels?.subject?.toLowerCase() ?? "cadastro"}.</p>
              </div>
              <Switch checked={form.requiresSubject} onCheckedChange={(v) => set("requiresSubject", v)} disabled={isPending} />
            </div>
          )}
        </div>

        <DialogFooter className="gap-2">
          <Button variant="outline" onClick={onClose} disabled={isPending}>Cancelar</Button>
          <Button onClick={handleSave} disabled={isPending}>{isPending ? "Salvando..." : "Salvar"}</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
