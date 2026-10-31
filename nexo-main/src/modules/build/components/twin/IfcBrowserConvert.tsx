import { useRef, useState } from "react";
import { Cpu, FileUp } from "lucide-react";
import { toast } from "sonner";
import { ApiError } from "@/services/api-client";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { fetchModelSource, uploadViewer, type BuildModelVersionDto } from "../../api/build-twin.api";
import { useInvalidateTwin } from "../../hooks/use-build-twin";
import { overallPercent, sha256Hex, sha256HexOfBytes, uploadConvertedViewer } from "../../lib/ifc-convert";
import { formatBytes, twinErrorMessage } from "../../lib/twin";
import { ProgressBar } from "./common";
import { loadIfcConverter } from "./load-ifc-converter";

const PHASE_LABEL = {
  download: "Baixando o arquivo do projeto", parse: "Lendo o IFC", geometry: "Gerando a geometria dos elementos",
  export: "Montando o GLB", upload: "Enviando a visualização",
};
type Phase = keyof typeof PHASE_LABEL;

/** A failure to obtain the stored source file (not of the conversion itself): the user can pick the file instead. */
class SourceUnavailable extends Error {}

/** Why the stored file could not be downloaded, in words the user can act on. */
function sourceFailureReason(e: unknown): string {
  if (e instanceof ApiError) {
    const text = twinErrorMessage(e, "o servidor não liberou o arquivo");
    return text.endsWith(".") ? text.slice(0, -1) : text;
  }
  if (e instanceof TypeError) return "o navegador bloqueou o download do armazenamento (por exemplo, por CORS)";
  return e instanceof Error && e.message ? e.message : "falha ao baixar o arquivo";
}

/**
 * "Gerar visualização 3D no navegador" for an IFC version Ready without viewer (no IfcConvert on the server).
 * The original IFC is fetched through a short-lived signed URL (GET models/versions/{id}/source) and converted by web-ifc in a
 * Web Worker. If that download fails (e.g. CORS on the storage URL), the user can pick the same .ifc instead — its SHA-256 is
 * compared with the version's before converting, as is the downloaded file's.
 */
export function IfcBrowserConvert({ projectId, version, enabled }: { projectId: string; version: BuildModelVersionDto; enabled: boolean }) {
  const invalidate = useInvalidateTwin(projectId);
  const inputRef = useRef<HTMLInputElement>(null);
  const [running, setRunning] = useState<{ phase: Phase; percent: number } | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [fallback, setFallback] = useState<string | null>(null);

  const sameHash = (hash: string | null) => !hash || !version.sha256 || hash.toLowerCase() === version.sha256.toLowerCase();

  const convert = async (file: File) => {
    const progress = (phase: Exclude<Phase, "download">, done: number, total: number) =>
      setRunning({ phase, percent: overallPercent(phase, done, total) });
    const { convertIfcToGlb } = await loadIfcConverter();
    const { glb, elements } = await convertIfcToGlb(file, progress);
    await uploadConvertedViewer(version.id, glb, uploadViewer, progress);
    invalidate();
    toast.success(`Visualização 3D gerada (${elements} elementos).`);
  };

  /** Downloads the version's stored IFC (signed URL) and checks it is the version's file. */
  const download = async (): Promise<File> => {
    try {
      const source = await fetchModelSource(version.id);
      const res = await fetch(source.url);
      if (!res.ok) throw new Error(`o armazenamento respondeu ${res.status}`);
      const bytes = await res.arrayBuffer();
      if (!sameHash(await sha256HexOfBytes(bytes))) throw new Error("o arquivo baixado não confere com o da versão");
      return new File([bytes], source.fileName || version.sourceFileName, { type: "application/octet-stream" });
    } catch (e) {
      throw new SourceUnavailable(sourceFailureReason(e));
    }
  };

  const runFromServer = async () => {
    setError(null);
    setRunning({ phase: "download", percent: 0 });
    try {
      const file = await download();
      setRunning({ phase: "parse", percent: 0 });
      await convert(file);
    } catch (e) {
      if (e instanceof SourceUnavailable) setFallback(e.message);
      else setError(twinErrorMessage(e, "Não foi possível gerar a visualização 3D."));
    } finally {
      setRunning(null);
    }
  };

  const runFromPicked = async (file: File | undefined) => {
    if (inputRef.current) inputRef.current.value = "";
    if (!file) return;
    setError(null);
    setRunning({ phase: "parse", percent: 0 });
    try {
      if (!sameHash(await sha256Hex(file)))
        throw new Error(`Este não é o arquivo da versão ${version.number} (${version.sourceFileName}). Escolha o mesmo .ifc enviado.`);
      await convert(file);
    } catch (e) {
      setError(twinErrorMessage(e, "Não foi possível gerar a visualização 3D."));
    } finally {
      setRunning(null);
    }
  };

  return (
    <div className="flex h-full flex-col items-center justify-center gap-3 p-4 text-center" data-testid="ifc-browser-convert">
      <Cpu className="h-6 w-6 text-muted-foreground" aria-hidden />
      <p className="max-w-md text-sm">
        O modelo IFC foi lido ({version.elementCount} elementos), mas ainda não tem arquivo de visualização 3D.
        Você pode gerá-lo aqui mesmo, no navegador, a partir do arquivo enviado <strong className="break-all">{version.sourceFileName}</strong> ({formatBytes(version.sizeBytes)}).
      </p>
      <input ref={inputRef} type="file" accept=".ifc" className="hidden" data-testid="ifc-convert-input"
        onChange={(e) => void runFromPicked(e.target.files?.[0])} />
      <Button type="button" disabled={!enabled || !!running} onClick={() => void runFromServer()}>
        <Cpu className="mr-1.5 h-4 w-4" />Gerar visualização 3D no navegador
      </Button>
      {!enabled && <p className="text-xs text-muted-foreground">O 3D não está habilitado para sua empresa.</p>}
      {fallback && enabled && (
        <div className="max-w-md space-y-2 rounded-md border border-amber-300 bg-amber-50 p-3 text-xs text-amber-900 dark:border-amber-800 dark:bg-amber-900/20 dark:text-amber-200"
          data-testid="ifc-convert-fallback">
          <p>
            Não foi possível baixar o arquivo do projeto guardado no Orken: {fallback}. Escolha o mesmo arquivo
            .ifc no seu computador para continuar.
          </p>
          <Button type="button" size="sm" variant="outline" disabled={!!running} onClick={() => inputRef.current?.click()}>
            <FileUp className="mr-1.5 h-4 w-4" />Escolher o arquivo .ifc
          </Button>
        </div>
      )}
      {error && <p role="alert" className="max-w-md break-words text-sm text-destructive">{error}</p>}
      <p className="max-w-md text-[11px] text-muted-foreground">
        Modelos grandes podem levar alguns minutos. A conversão roda no seu navegador: só a visualização (GLB) é enviada.
      </p>

      <Dialog open={!!running}>
        <DialogContent className="sm:max-w-md" onInteractOutside={(e) => e.preventDefault()} onEscapeKeyDown={(e) => e.preventDefault()}>
          <DialogHeader>
            <DialogTitle>Gerando a visualização 3D</DialogTitle>
            <DialogDescription>Mantenha esta aba aberta até terminar.</DialogDescription>
          </DialogHeader>
          {running && <ProgressBar value={running.percent} label={PHASE_LABEL[running.phase]} />}
        </DialogContent>
      </Dialog>
    </div>
  );
}
