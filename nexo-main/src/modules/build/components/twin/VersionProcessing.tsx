import { Loader2, RotateCcw } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { useModelVersion, useReprocessVersion } from "../../hooks/use-build-twin";
import { GEOMETRY_LABEL, VERSION_STATUS, formatBytes, twinErrorMessage } from "../../lib/twin";
import { StatusPill } from "../commercial/StatusPill";

interface Props {
  projectId: string;
  versionId: string;
  build3DEnabled: boolean;
  onContinue?: () => void;
}

/** Step 3: the server's processing of the version — polled every 3 s while Queued/Processing. */
export function VersionProcessing({ projectId, versionId, build3DEnabled, onContinue }: Props) {
  const { data: v, isError } = useModelVersion(versionId);
  const reprocess = useReprocessVersion(projectId);

  if (isError) return <p role="alert" className="text-sm text-destructive">Não foi possível consultar a versão.</p>;
  if (!v) return <p className="text-sm text-muted-foreground">Carregando…</p>;
  const meta = VERSION_STATUS[v.status];
  const working = v.status === "Queued" || v.status === "Processing" || v.status === "Uploaded";

  return (
    <div className="space-y-3" data-testid="version-processing">
      <div className="flex flex-wrap items-center gap-2">
        <StatusPill label={meta.label} tone={meta.tone} />
        <span className="min-w-0 break-all text-sm">{v.sourceFileName}</span>
        <span className="text-xs text-muted-foreground">v{v.number} · {formatBytes(v.sizeBytes)}</span>
      </div>
      {working && (
        <p className="flex items-center gap-2 text-sm text-muted-foreground">
          <Loader2 className="h-4 w-4 animate-spin" aria-hidden />
          Lendo o modelo: pavimentos, ambientes e elementos. Esta tela atualiza sozinha.
        </p>
      )}
      {v.statusMessage && <p className="break-words text-sm">{v.statusMessage}</p>}
      {v.status === "Ready" && (
        <dl className="grid grid-cols-2 gap-2 text-sm sm:grid-cols-4">
          <div><dt className="text-xs text-muted-foreground">Elementos</dt><dd className="font-semibold tabular-nums">{v.elementCount}</dd></div>
          <div><dt className="text-xs text-muted-foreground">Pavimentos</dt><dd className="font-semibold tabular-nums">{v.storeyCount}</dd></div>
          <div><dt className="text-xs text-muted-foreground">Geometria</dt><dd>{GEOMETRY_LABEL[v.geometrySource] ?? v.geometrySource}</dd></div>
          <div><dt className="text-xs text-muted-foreground">Esquema</dt><dd>{v.schema ?? "—"}</dd></div>
        </dl>
      )}
      <div className="flex flex-wrap gap-2">
        {v.status === "Failed" && (
          <Button type="button" variant="outline" disabled={!build3DEnabled || reprocess.isPending}
            onClick={() => reprocess.mutate(v.id, {
              onSuccess: () => toast.success("Versão enviada de novo para processamento."),
              onError: (e) => toast.error(twinErrorMessage(e, "Não foi possível reprocessar.")),
            })}>
            <RotateCcw className="mr-1.5 h-4 w-4" />Reprocessar
          </Button>
        )}
        {v.status === "Ready" && onContinue && <Button type="button" onClick={onContinue}>Continuar: vincular etapas</Button>}
      </div>
    </div>
  );
}
