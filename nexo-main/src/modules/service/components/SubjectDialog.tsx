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
import { useCustomers } from "@/modules/customers/hooks/use-customers";
import type {
  CreateSubjectRequest,
  SvcSubjectDto,
  SvcSubjectKind,
  UpdateSubjectRequest,
} from "../api/service.api";
import { useCreateSubject, useUpdateSubject } from "../hooks/useSubjects";
import { useServicePreset } from "../context/ServicePresetContext";

interface SubjectDialogProps {
  open: boolean;
  onClose: () => void;
  subject?: SvcSubjectDto | null;
}

const KIND_LABELS: Record<SvcSubjectKind, string> = {
  Pet: "Pet",
  Vehicle: "Veículo",
  Student: "Aluno",
  Dependent: "Dependente",
  Other: "Outro",
};

export function SubjectDialog({ open, onClose, subject }: SubjectDialogProps) {
  const { labels, capabilities } = useServicePreset();
  const term = labels?.subject ?? "Cadastro";
  const isEdit = !!subject;

  const { data: customers, isLoading: loadingCustomers } = useCustomers(false);
  const create = useCreateSubject();
  const update = useUpdateSubject(subject?.id ?? "");
  const isPending = create.isPending || update.isPending;

  const defaultKind: SvcSubjectKind = capabilities?.subjectKind ?? "Other";

  const [customerId, setCustomerId] = useState("");
  const [kind, setKind] = useState<SvcSubjectKind>(defaultKind);
  const [displayName, setDisplayName] = useState("");
  const [notes, setNotes] = useState("");

  useEffect(() => {
    if (!open) return;
    setCustomerId(subject?.customerId ?? "");
    setKind(subject?.kind ?? defaultKind);
    setDisplayName(subject?.displayName ?? "");
    setNotes(subject?.notes ?? "");
  }, [open, subject, defaultKind]);

  const handleSave = async () => {
    if (!isEdit && !customerId) { toast.error("Selecione o cliente."); return; }
    if (!displayName.trim()) { toast.error("Nome é obrigatório."); return; }

    try {
      if (isEdit) {
        const body: UpdateSubjectRequest = {
          kind,
          displayName: displayName.trim(),
          notes: notes.trim() || null,
        };
        await update.mutateAsync(body);
        toast.success(`${term} atualizado.`);
      } else {
        const body: CreateSubjectRequest = {
          customerId,
          kind,
          displayName: displayName.trim(),
          notes: notes.trim() || null,
        };
        await create.mutateAsync(body);
        toast.success(`${term} cadastrado.`);
      }
      onClose();
    } catch {
      toast.error(`Não foi possível salvar o ${term.toLowerCase()}.`);
    }
  };

  const customerName = customers?.find((c) => c.id === subject?.customerId)?.name;

  return (
    <Dialog open={open} onOpenChange={(v) => { if (!v && !isPending) onClose(); }}>
      <DialogContent className="max-w-lg">
        <DialogHeader>
          <DialogTitle>{isEdit ? `Editar ${term.toLowerCase()}` : `Novo ${term.toLowerCase()}`}</DialogTitle>
        </DialogHeader>

        <div className="space-y-3 py-1">
          <div className="space-y-1.5">
            <Label>Cliente *</Label>
            {isEdit ? (
              <Input value={customerName ?? "—"} disabled readOnly />
            ) : (
              <Select value={customerId} onValueChange={setCustomerId} disabled={isPending || loadingCustomers}>
                <SelectTrigger>
                  <SelectValue placeholder={loadingCustomers ? "Carregando..." : "Selecione o cliente"} />
                </SelectTrigger>
                <SelectContent>
                  {(customers ?? []).map((c) => (
                    <SelectItem key={c.id} value={c.id}>{c.name}</SelectItem>
                  ))}
                </SelectContent>
              </Select>
            )}
          </div>

          <div className="grid grid-cols-2 gap-3">
            <div className="space-y-1.5">
              <Label>Tipo</Label>
              <Select value={kind} onValueChange={(v) => setKind(v as SvcSubjectKind)} disabled={isPending}>
                <SelectTrigger><SelectValue /></SelectTrigger>
                <SelectContent>
                  {(Object.keys(KIND_LABELS) as SvcSubjectKind[]).map((k) => (
                    <SelectItem key={k} value={k}>{KIND_LABELS[k]}</SelectItem>
                  ))}
                </SelectContent>
              </Select>
            </div>
            <div className="space-y-1.5">
              <Label htmlFor="subj-name">Nome *</Label>
              <Input id="subj-name" value={displayName} onChange={(e) => setDisplayName(e.target.value)}
                maxLength={200} disabled={isPending} autoFocus />
            </div>
          </div>

          <div className="space-y-1.5">
            <Label htmlFor="subj-notes">Observações</Label>
            <Textarea id="subj-notes" value={notes} rows={2} onChange={(e) => setNotes(e.target.value)}
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
