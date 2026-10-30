import { useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { formatDateTime } from "@/lib/formatters";
import { useAuth } from "@/modules/auth/context/AuthContext";
import {
  TAX_KEYS, evidenceFileUrl, getEvidenceStorage, listEvidenceFiles, revokeEvidenceFile, setLegalHold, uploadEvidenceFile,
  type EvidenceType, type FileOwner, type TaxStoredFileDto,
} from "../api/tax.api";
import { EVIDENCE_TYPES, FILE_STATUS, labelOf, toneOf } from "../lib/labels";
import { ToneBadge } from "./TaxBadges";
import { DownloadButton, FilePicker, toastError, useReasonDialog } from "./TaxCommon";
import { Field } from "./TaxShell";

/** One stored file: name, size, SHA-256 of its bytes, status, download (authorised) and, for the board, revoked copies too. */
export function StoredFileLine({ file, canReadRevoked }: Readonly<{ file: TaxStoredFileDto; canReadRevoked: boolean }>) {
  return (
    <div className="min-w-0 space-y-0.5 text-xs">
      <p className="flex flex-wrap items-center gap-2">
        <span className="break-all font-medium">{file.originalFileName}</span>
        <ToneBadge label={labelOf(FILE_STATUS, file.status)} tone={toneOf(FILE_STATUS, file.status)} />
        {file.legalHold && <ToneBadge label="Retenção legal" tone="warning" />}
        {file.scanStatus === "NotScanned" && <span className="text-muted-foreground">não verificado por antivírus</span>}
      </p>
      <p className="break-all font-mono text-muted-foreground">SHA-256 {file.sha256}</p>
      <p className="text-muted-foreground">
        {(file.sizeBytes / 1024).toFixed(0)} KB · v{file.version} · {file.uploadedByName}, {formatDateTime(file.uploadedAt)}
        {file.revokedAt ? ` · revogado por ${file.revokedByName}: ${file.revocationReason}` : ""}
      </p>
      {(file.status !== "Revoked" || canReadRevoked) && (
        <DownloadButton path={evidenceFileUrl(file.id)} fileName={file.originalFileName} label="Baixar" variant="ghost" />
      )}
    </div>
  );
}

/** Private files of a credit, a compliance assessment or a split settlement: list, upload, revoke and legal hold — never deleted. */
export function EvidenceFiles({ owner, ownerId, defaultType = "Other" }: Readonly<{ owner: FileOwner; ownerId: string; defaultType?: EvidenceType }>) {
  const qc = useQueryClient();
  const { session } = useAuth();
  const isDirector = session?.role === "diretoria";
  const [file, setFile] = useState<File | null>(null);
  const [type, setType] = useState<EvidenceType>(defaultType);
  const [description, setDescription] = useState("");
  const [reasonDialog, askReason] = useReasonDialog();
  const storage = useQuery({ queryKey: TAX_KEYS.storage, queryFn: getEvidenceStorage, staleTime: 60_000 });
  const files = useQuery({ queryKey: TAX_KEYS.files(owner, ownerId), queryFn: () => listEvidenceFiles(owner, ownerId) });
  const refresh = () => void qc.invalidateQueries({ queryKey: TAX_KEYS.files(owner, ownerId) });
  const upload = useMutation({
    mutationFn: () => uploadEvidenceFile(owner, ownerId, type, file!, description.trim() || null),
    onSuccess: () => { toast.success("Arquivo guardado no armazenamento privado."); setFile(null); setDescription(""); refresh(); },
    onError: toastError,
  });
  const revoke = useMutation({
    mutationFn: ({ id, reason }: { id: string; reason: string }) => revokeEvidenceFile(id, reason),
    onSuccess: () => { toast.success("Arquivo revogado (continua registrado)."); refresh(); },
    onError: toastError,
  });
  const hold = useMutation({
    mutationFn: ({ id, value, reason }: { id: string; value: boolean; reason: string }) => setLegalHold(id, value, reason),
    onSuccess: refresh,
    onError: toastError,
  });

  return (
    <div className="space-y-3">
      {reasonDialog}
      {files.data?.length === 0 && <p className="text-xs text-muted-foreground">Nenhum arquivo anexado.</p>}
      <ul className="space-y-2">
        {files.data?.map((f) => (
          <li key={f.file.id} className="flex flex-col gap-2 rounded-md border border-border p-2 sm:flex-row sm:items-start sm:justify-between">
            <StoredFileLine file={f.file} canReadRevoked={isDirector} />
            <div className="flex flex-wrap gap-1">
              {f.file.status === "Active" && (
                <Button type="button" size="sm" variant="ghost" onClick={() => askReason({
                  title: "Revogar arquivo", description: "O arquivo deixa de valer, mas continua registrado e auditável.", confirmLabel: "Revogar", danger: true,
                  onConfirm: (reason) => revoke.mutate({ id: f.file.id, reason }),
                })}>Revogar</Button>
              )}
              {isDirector && (
                <Button type="button" size="sm" variant="ghost" onClick={() => askReason({
                  title: f.file.legalHold ? "Retirar retenção legal" : "Aplicar retenção legal",
                  description: "Enquanto retido, nenhuma política de retenção torna o arquivo elegível para descarte.",
                  onConfirm: (reason) => hold.mutate({ id: f.file.id, value: !f.file.legalHold, reason }),
                })}>{f.file.legalHold ? "Retirar retenção legal" : "Reter legalmente"}</Button>
              )}
            </div>
          </li>
        ))}
      </ul>
      {storage.data && !storage.data.available && <p className="text-xs text-warning">{storage.data.message}</p>}
      {storage.data?.available && (
        <form className="grid gap-2 sm:grid-cols-2" onSubmit={(e) => { e.preventDefault(); if (file) upload.mutate(); }}>
          <Field id={`ef-type-${ownerId}`} label="Tipo">
            <Select value={type} onValueChange={(v) => setType(v as EvidenceType)}>
              <SelectTrigger id={`ef-type-${ownerId}`}><SelectValue /></SelectTrigger>
              <SelectContent>{(Object.keys(EVIDENCE_TYPES) as EvidenceType[]).map((k) => <SelectItem key={k} value={k}>{EVIDENCE_TYPES[k]}</SelectItem>)}</SelectContent>
            </Select>
          </Field>
          <Field id={`ef-desc-${ownerId}`} label="Descrição (opcional)">
            <Input id={`ef-desc-${ownerId}`} maxLength={1000} value={description} onChange={(e) => setDescription(e.target.value)} />
          </Field>
          <div className="flex flex-wrap items-center gap-2 sm:col-span-2">
            <FilePicker id={`ef-file-${ownerId}`} file={file} onChange={setFile} />
            <Button type="submit" size="sm" disabled={!file || upload.isPending}>{upload.isPending ? "Enviando…" : "Anexar"}</Button>
          </div>
          <p className="text-xs text-muted-foreground sm:col-span-2">
            Até {Math.round(storage.data.maxBytes / (1024 * 1024))} MB. O tipo é conferido pelo conteúdo e o hash guardado é o SHA-256 dos bytes.
          </p>
        </form>
      )}
    </div>
  );
}
