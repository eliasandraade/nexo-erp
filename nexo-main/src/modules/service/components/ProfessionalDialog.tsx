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
import type { SaveProfessionalRequest, SvcProfessionalDto } from "../api/service.api";
import { useCreateProfessional, useUpdateProfessional } from "../hooks/useProfessionals";
import { useServicePreset } from "../context/ServicePresetContext";

interface ProfessionalDialogProps {
  open: boolean;
  onClose: () => void;
  /** When set, the dialog edits this professional; otherwise it creates a new one. */
  professional?: SvcProfessionalDto | null;
}

interface FormState {
  name: string;
  role: string;
  specialty: string;
  phone: string;
  email: string;
  color: string;
  defaultCommissionPercent: string;
}

const EMPTY: FormState = {
  name: "",
  role: "",
  specialty: "",
  phone: "",
  email: "",
  color: "#5B4DFF",
  defaultCommissionPercent: "",
};

export function ProfessionalDialog({ open, onClose, professional }: ProfessionalDialogProps) {
  const { labels, capabilities } = useServicePreset();
  const term = labels?.professional ?? "Profissional";
  const isEdit = !!professional;

  const create = useCreateProfessional();
  const update = useUpdateProfessional(professional?.id ?? "");
  const isPending = create.isPending || update.isPending;

  const [form, setForm] = useState<FormState>(EMPTY);

  useEffect(() => {
    if (!open) return;
    setForm(
      professional
        ? {
            name: professional.name,
            role: professional.role ?? "",
            specialty: professional.specialty ?? "",
            phone: professional.phone ?? "",
            email: professional.email ?? "",
            color: professional.color ?? "#5B4DFF",
            defaultCommissionPercent:
              professional.defaultCommissionPercent?.toString() ?? "",
          }
        : EMPTY
    );
  }, [open, professional]);

  const set = (field: keyof FormState, value: string) =>
    setForm((prev) => ({ ...prev, [field]: value }));

  const buildBody = (): SaveProfessionalRequest | string => {
    if (!form.name.trim()) return "Nome é obrigatório.";
    if (form.email && !form.email.includes("@")) return "E-mail inválido.";
    let commission: number | null = null;
    if (form.defaultCommissionPercent.trim()) {
      commission = Number(form.defaultCommissionPercent);
      if (Number.isNaN(commission) || commission < 0 || commission > 100)
        return "Comissão deve estar entre 0 e 100.";
    }
    return {
      name: form.name.trim(),
      role: form.role.trim() || null,
      specialty: form.specialty.trim() || null,
      phone: form.phone.trim() || null,
      email: form.email.trim() || null,
      color: form.color || null,
      defaultCommissionPercent: capabilities?.commissions ? commission : null,
    };
  };

  const handleSave = async () => {
    const body = buildBody();
    if (typeof body === "string") {
      toast.error(body);
      return;
    }
    try {
      if (isEdit) {
        await update.mutateAsync(body);
        toast.success(`${term} atualizado.`);
      } else {
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
            <Label htmlFor="prof-name">Nome *</Label>
            <Input id="prof-name" value={form.name} onChange={(e) => set("name", e.target.value)}
              maxLength={200} disabled={isPending} autoFocus />
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="prof-role">Função</Label>
            <Input id="prof-role" value={form.role} onChange={(e) => set("role", e.target.value)}
              maxLength={100} disabled={isPending} />
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="prof-specialty">Especialidade</Label>
            <Input id="prof-specialty" value={form.specialty} onChange={(e) => set("specialty", e.target.value)}
              maxLength={150} disabled={isPending} />
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="prof-phone">Telefone</Label>
            <Input id="prof-phone" value={form.phone} onChange={(e) => set("phone", e.target.value)}
              maxLength={30} disabled={isPending} />
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="prof-email">E-mail</Label>
            <Input id="prof-email" type="email" value={form.email} onChange={(e) => set("email", e.target.value)}
              maxLength={200} disabled={isPending} />
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="prof-color">Cor na agenda</Label>
            <div className="flex items-center gap-2">
              <input id="prof-color" type="color" value={form.color}
                onChange={(e) => set("color", e.target.value)} disabled={isPending}
                className="h-9 w-12 cursor-pointer rounded border border-border bg-transparent p-0.5" />
              <span className="text-[12px] text-muted-foreground">{form.color}</span>
            </div>
          </div>
          {capabilities?.commissions && (
            <div className="space-y-1.5">
              <Label htmlFor="prof-commission">Comissão padrão (%)</Label>
              <Input id="prof-commission" type="number" min={0} max={100} step="0.5"
                value={form.defaultCommissionPercent}
                onChange={(e) => set("defaultCommissionPercent", e.target.value)} disabled={isPending} />
            </div>
          )}
        </div>

        <DialogFooter className="gap-2">
          <Button variant="outline" onClick={onClose} disabled={isPending}>Cancelar</Button>
          <Button onClick={handleSave} disabled={isPending}>
            {isPending ? "Salvando..." : "Salvar"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
