import { useState } from "react";
import { toast } from "sonner";
import { Trash2, FileText } from "lucide-react";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import { Textarea } from "@/components/ui/textarea";
import { Skeleton } from "@/components/ui/skeleton";
import { formatDateTime } from "@/lib/formatters";
import type { SvcSubjectDto } from "../api/service.api";
import { useRecords, useCreateRecord, useDeleteRecord } from "../hooks/useRecords";

interface SubjectRecordsDialogProps {
  open: boolean;
  onClose: () => void;
  subject: SvcSubjectDto | null;
}

/**
 * Append-only record timeline for a subject. Text records only in v1 — attachment upload is
 * deferred (the backend supports attachment refs, but the upload UX lands later).
 */
export function SubjectRecordsDialog({ open, onClose, subject }: SubjectRecordsDialogProps) {
  const subjectId = subject?.id;
  const { data: records, isLoading, isError } = useRecords("Subject", subjectId);
  const createRecord = useCreateRecord("Subject", subjectId ?? "");
  const deleteRecord = useDeleteRecord("Subject", subjectId ?? "");

  const [text, setText] = useState("");

  const handleAdd = async () => {
    if (!subjectId || !text.trim()) { toast.error("Escreva uma anotação."); return; }
    try {
      await createRecord.mutateAsync({ contextType: "Subject", contextId: subjectId, text: text.trim() });
      setText("");
      toast.success("Registro adicionado.");
    } catch {
      toast.error("Não foi possível adicionar o registro.");
    }
  };

  const handleDelete = async (id: string) => {
    try {
      await deleteRecord.mutateAsync(id);
      toast.success("Registro removido.");
    } catch {
      toast.error("Não foi possível remover o registro.");
    }
  };

  return (
    <Dialog open={open} onOpenChange={(v) => { if (!v) onClose(); }}>
      <DialogContent className="max-w-lg">
        <DialogHeader>
          <DialogTitle>Registros — {subject?.displayName ?? ""}</DialogTitle>
        </DialogHeader>

        <div className="space-y-3 py-1">
          <div className="space-y-2">
            <Textarea
              value={text}
              onChange={(e) => setText(e.target.value)}
              placeholder="Nova anotação..."
              rows={2}
              maxLength={4000}
              disabled={createRecord.isPending}
            />
            <div className="flex justify-end">
              <Button size="sm" onClick={handleAdd} disabled={createRecord.isPending || !text.trim()}>
                {createRecord.isPending ? "Adicionando..." : "Adicionar"}
              </Button>
            </div>
          </div>

          <div className="max-h-[320px] space-y-2 overflow-y-auto">
            {isLoading && [1, 2].map((i) => <Skeleton key={i} className="h-14 w-full" />)}

            {isError && (
              <p className="py-6 text-center text-[12.5px] text-muted-foreground">
                Não foi possível carregar os registros.
              </p>
            )}

            {!isLoading && !isError && (records?.length ?? 0) === 0 && (
              <div className="flex flex-col items-center py-8 text-center">
                <FileText className="mb-2 h-5 w-5 text-muted-foreground" />
                <p className="text-[12.5px] text-muted-foreground">Nenhum registro ainda.</p>
              </div>
            )}

            {records?.map((rec) => (
              <div key={rec.id} className="group rounded-md border border-border bg-card p-3">
                <div className="flex items-start justify-between gap-3">
                  <p className="whitespace-pre-wrap text-[13px] text-foreground">{rec.text}</p>
                  <button
                    onClick={() => handleDelete(rec.id)}
                    disabled={deleteRecord.isPending}
                    className="shrink-0 text-muted-foreground opacity-0 transition-opacity hover:text-destructive group-hover:opacity-100"
                    aria-label="Remover registro"
                  >
                    <Trash2 className="h-3.5 w-3.5" />
                  </button>
                </div>
                <p className="mt-1.5 text-[11px] text-muted-foreground">{formatDateTime(rec.createdAt)}</p>
              </div>
            ))}
          </div>
        </div>
      </DialogContent>
    </Dialog>
  );
}
