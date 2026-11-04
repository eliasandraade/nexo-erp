import { useEffect, useState } from "react";
import { AlertTriangle, Camera } from "lucide-react";
import { toast } from "sonner";
import { cn } from "@/lib/utils";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import type {
  BuildInspectionItemDto, BuildInspectionItemResult, BuildNcSeverity, RecordBuildInspectionItemRequest,
} from "../../api/build-quality.api";
import { ITEM_RESULTS, NC_SEVERITIES, defaultItemSeverity, severityInfo } from "../../lib/quality";
import { PhotoStrip, PrivateUploadButton } from "./PrivateUploadButton";

interface Props {
  item: BuildInspectionItemDto;
  readOnly: boolean;
  busy: boolean;
  onRecord: (req: RecordBuildInspectionItemRequest) => Promise<unknown>;
}

const RESULT_STYLE: Record<Exclude<BuildInspectionItemResult, "Pending">, string> = {
  Ok:            "border-emerald-600 bg-emerald-600 text-white",
  NotOk:         "border-red-600 bg-red-600 text-white",
  NotApplicable: "border-gray-500 bg-gray-500 text-white",
};

/** One checklist line, built for a phone on site: big result buttons, comment, photos and the NC severity. */
export function InspectionItemCard({ item, readOnly, busy, onRecord }: Props) {
  const [comment, setComment] = useState(item.comment ?? "");
  useEffect(() => { setComment(item.comment ?? ""); }, [item.comment]);

  const photoKeys = Array.isArray(item.photoKeys) ? item.photoKeys : [];
  const judged = item.result === "Ok" || item.result === "NotOk";
  const missingPhoto = item.requiresPhoto && judged && photoKeys.length === 0;

  const record = (patch: Partial<RecordBuildInspectionItemRequest>) => {
    const result = patch.result ?? item.result;
    void onRecord({
      result,
      comment:   (patch.comment !== undefined ? patch.comment : comment.trim()) || null,
      photoKeys: patch.photoKeys ?? photoKeys,
      severity:  result === "NotOk" ? (patch.severity !== undefined ? patch.severity : item.severity) : null,
    }).catch(() => undefined);
  };

  const saveComment = () => {
    if ((item.comment ?? "") === comment.trim()) return;
    record({ comment: comment.trim() || null });
  };

  return (
    <li className={cn("space-y-3 rounded-md border bg-card p-3",
      missingPhoto ? "border-red-300 dark:border-red-800" : "border-border")}
      data-testid={`inspection-item-${item.id}`}>
      <div className="space-y-1">
        <p className="text-sm font-medium">
          <span className="mr-1 text-muted-foreground tabular-nums">{item.order + 1}.</span>{item.text}
        </p>
        <div className="flex flex-wrap gap-1.5 text-[11px]">
          {item.required
            ? <span className="rounded-full bg-muted px-2 py-0.5 font-medium">Obrigatório</span>
            : <span className="rounded-full bg-muted px-2 py-0.5 text-muted-foreground">Opcional</span>}
          {item.requiresPhoto && (
            <span className="inline-flex items-center gap-1 rounded-full bg-amber-100 px-2 py-0.5 font-semibold text-amber-800 dark:bg-amber-900/30 dark:text-amber-300">
              <Camera className="h-3 w-3" /> foto obrigatória
            </span>
          )}
          {item.nonConformityId && (
            <span className="rounded-full bg-red-100 px-2 py-0.5 font-medium text-red-700 dark:bg-red-900/30 dark:text-red-400">
              Abriu não conformidade
            </span>
          )}
        </div>
      </div>

      <div className="grid grid-cols-3 gap-2" role="group" aria-label={`Resultado de ${item.text}`}>
        {ITEM_RESULTS.map((r) => {
          const active = item.result === r.value;
          return (
            <button key={r.value} type="button" disabled={readOnly || busy} aria-pressed={active}
              onClick={() => record({ result: active ? "Pending" : r.value })}
              className={cn(
                "min-h-11 rounded-md border px-2 text-sm font-semibold transition-colors disabled:opacity-60",
                active ? RESULT_STYLE[r.value] : "border-input bg-background hover:bg-muted",
              )}>
              {r.label}
            </button>
          );
        })}
      </div>

      {item.result === "NotOk" && (
        <div className="space-y-1">
          <Label htmlFor={`sev-${item.id}`} className="text-xs">Gravidade da não conformidade</Label>
          <select id={`sev-${item.id}`} disabled={readOnly || busy} value={item.severity ?? ""}
            onChange={(e) => record({ severity: (e.target.value || null) as BuildNcSeverity | null })}
            className="h-10 w-full rounded-md border border-input bg-background px-2 text-sm">
            <option value="">Padrão ({severityInfo(defaultItemSeverity(item.required)).label})</option>
            {NC_SEVERITIES.map((s) => <option key={s.value} value={s.value}>{s.label}</option>)}
          </select>
          <p className="text-[11px] text-muted-foreground">Ao concluir, este item abre uma não conformidade.</p>
        </div>
      )}

      <div className="space-y-1">
        <Label htmlFor={`comment-${item.id}`} className="text-xs">Comentário</Label>
        <Textarea id={`comment-${item.id}`} value={comment} rows={2} disabled={readOnly} maxLength={2000}
          onChange={(e) => setComment(e.target.value)} onBlur={saveComment} className="text-sm" />
      </div>

      <div className="space-y-2">
        <PhotoStrip urls={item.photoUrls} alt={`Foto de ${item.text}`} />
        {!readOnly && (
          <PrivateUploadButton context="build-quality" capture label="Foto" disabled={busy}
            testId={`item-photo-input-${item.id}`}
            onUploaded={({ key }) => { record({ photoKeys: [...photoKeys, key] }); toast.success("Foto enviada."); }} />
        )}
        {missingPhoto && (
          <p role="alert" className="flex items-center gap-1 text-xs font-medium text-destructive">
            <AlertTriangle className="h-3.5 w-3.5" /> Foto obrigatória: envie ao menos uma foto deste item.
          </p>
        )}
      </div>
    </li>
  );
}
