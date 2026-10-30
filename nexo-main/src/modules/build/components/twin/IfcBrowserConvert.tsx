import { useRef, useState } from "react";
import { Cpu, FileUp } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { uploadViewer, type BuildModelVersionDto } from "../../api/build-twin.api";
import { useInvalidateTwin } from "../../hooks/use-build-twin";
import { overallPercent, sha256Hex, uploadConvertedViewer } from "../../lib/ifc-convert";
import { formatBytes, twinErrorMessage } from "../../lib/twin";
import { ProgressBar } from "./common";
import { loadIfcConverter } from "./load-ifc-converter";

const PHASE_LABEL = { parse: "Lendo o IFC", geometry: "Gerando a geometria dos elementos", export: "Montando o GLB", upload: "Enviando a visualização" };

/**
 * "Gerar visualização 3D no navegador" for an IFC version Ready without viewer (no IfcConvert on the server).
 * The API has no internal route to download the version's source file, so the user picks the same .ifc again; its SHA-256
 * is compared with the version's before converting. web-ifc runs in a Web Worker; the dialog shows the progress.
 */
export function IfcBrowserConvert({ projectId, version, enabled }: { projectId: string; version: BuildModelVersionDto; enabled: boolean }) {
  const invalidate = useInvalidateTwin(projectId);
  const inputRef = useRef<HTMLInputElement>(null);
  const [running, setRunning] = useState<{ phase: keyof typeof PHASE_LABEL; percent: number } | null>(null);
  const [error, setError] = useState<string | null>(null);

  const run = async (file: File | undefined) => {
    if (inputRef.current) inputRef.current.value = "";
    if (!file) return;
    setError(null);
    setRunning({ phase: "parse", percent: 0 });
    try {
      const hash = await sha256Hex(file);
      if (hash && version.sha256 && hash.toLowerCase() !== version.sha256.toLowerCase())
        throw new Error(`Este não é o arquivo da versão ${version.number} (${version.sourceFileName}). Escolha o mesmo .ifc enviado.`);
      const progress = (phase: keyof typeof PHASE_LABEL, done: number, total: number) =>
        setRunning({ phase, percent: overallPercent(phase, done, total) });
      const { convertIfcToGlb } = await loadIfcConverter();
      const { glb, elements } = await convertIfcToGlb(file, progress);
      await uploadConvertedViewer(version.id, glb, uploadViewer, progress);
      invalidate();
      toast.success(`Visualização 3D gerada (${elements} elementos).`);
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
        Você pode gerá-lo aqui mesmo, no navegador: escolha o mesmo arquivo <strong className="break-all">{version.sourceFileName}</strong> ({formatBytes(version.sizeBytes)}).
      </p>
      <input ref={inputRef} type="file" accept=".ifc" className="hidden" data-testid="ifc-convert-input"
        onChange={(e) => void run(e.target.files?.[0])} />
      <Button type="button" disabled={!enabled || !!running} onClick={() => inputRef.current?.click()}>
        <FileUp className="mr-1.5 h-4 w-4" />Gerar visualização 3D no navegador
      </Button>
      {!enabled && <p className="text-xs text-muted-foreground">O 3D não está habilitado para sua empresa.</p>}
      {error && <p role="alert" className="max-w-md break-words text-sm text-destructive">{error}</p>}
      <p className="max-w-md text-[11px] text-muted-foreground">Modelos grandes podem levar alguns minutos. O arquivo do projeto não sai do seu computador: só a visualização (GLB) é enviada.</p>

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
