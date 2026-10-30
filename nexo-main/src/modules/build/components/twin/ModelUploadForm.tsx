import { useRef, useState } from "react";
import { AlertTriangle, Upload } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { ApiError } from "@/services/api-client";
import {
  uploadModel, uploadModelVersion, type BuildModelDiscipline, type BuildModelUploadResultDto,
} from "../../api/build-twin.api";
import { useInvalidateTwin } from "../../hooks/use-build-twin";
import {
  BUILD_3D_DISABLED_CODE, BUILD_3D_DISABLED_MESSAGE, DISCIPLINES, MODEL_ACCEPT, MODEL_MAX_UPLOAD_MB, RVT_DWG_INSTRUCTIONS,
  formatBytes, twinErrorMessage,
} from "../../lib/twin";
import { ProgressBar, selectClass } from "./common";

interface Props {
  projectId: string;
  /** New version of an existing model (no name/discipline). */
  modelId?: string;
  onUploaded: (result: BuildModelUploadResultDto) => void;
  onCancel?: () => void;
}

const isRvtDwg = (name: string) => /\.(rvt|rfa|dwg)$/i.test(name);

/** Step 2: name, discipline and the file — with the size limit, upload progress and the server's refusals in Portuguese. */
export function ModelUploadForm({ projectId, modelId, onUploaded, onCancel }: Props) {
  const invalidate = useInvalidateTwin(projectId);
  const fileRef = useRef<HTMLInputElement>(null);
  const [name, setName] = useState("");
  const [discipline, setDiscipline] = useState<BuildModelDiscipline>("Arquitetura");
  const [file, setFile] = useState<File | null>(null);
  const [progress, setProgress] = useState<number | null>(null);
  const [error, setError] = useState<{ text: string; disabled3D?: boolean } | null>(null);

  const tooBig = !!file && file.size > MODEL_MAX_UPLOAD_MB * 1024 * 1024;
  const proprietary = !!file && isRvtDwg(file.name);
  const busy = progress !== null;

  const submit = async () => {
    if (!file || tooBig || proprietary) return;
    setError(null);
    setProgress(0);
    const onProgress = (loaded: number, total: number) => setProgress(total > 0 ? (loaded / total) * 100 : 0);
    try {
      const result = modelId
        ? await uploadModelVersion(modelId, file, onProgress)
        : await uploadModel(projectId, { file, name, discipline }, onProgress);
      invalidate();
      onUploaded(result);
    } catch (e) {
      if (e instanceof ApiError && e.status === 403 && e.code === BUILD_3D_DISABLED_CODE) {
        setError({ text: BUILD_3D_DISABLED_MESSAGE, disabled3D: true });
      } else {
        // 422 model_format_rejected (RVT/DWG) is recorded as a Rejected version: refresh the list
        if (e instanceof ApiError && e.code === "model_format_rejected") invalidate();
        setError({ text: twinErrorMessage(e) });
      }
    } finally {
      setProgress(null);
    }
  };

  return (
    <div className="space-y-3">
      {!modelId && (
        <div className="grid gap-3 sm:grid-cols-2">
          <div className="space-y-1">
            <Label htmlFor="twin-model-name">Nome do modelo</Label>
            <Input id="twin-model-name" value={name} onChange={(e) => setName(e.target.value)} placeholder="Ex.: Projeto arquitetônico" maxLength={200} />
          </div>
          <div className="space-y-1">
            <Label htmlFor="twin-model-discipline">Disciplina</Label>
            <select id="twin-model-discipline" className={selectClass} value={discipline}
              onChange={(e) => setDiscipline(e.target.value as BuildModelDiscipline)}>
              {DISCIPLINES.map((d) => <option key={d.value} value={d.value}>{d.label}</option>)}
            </select>
          </div>
        </div>
      )}
      <div className="space-y-1">
        <Label htmlFor="twin-model-file">Arquivo do projeto</Label>
        <Input id="twin-model-file" ref={fileRef} type="file" accept={MODEL_ACCEPT} disabled={busy}
          onChange={(e) => { setFile(e.target.files?.[0] ?? null); setError(null); }} />
        <p className="text-[11px] text-muted-foreground">
          IFC, GLB/glTF, DXF, PDF ou imagem — até {MODEL_MAX_UPLOAD_MB} MB. O arquivo fica privado: o cliente nunca recebe o projeto original.
        </p>
        {file && <p className="text-xs text-muted-foreground">{file.name} · {formatBytes(file.size)}</p>}
        {tooBig && <p role="alert" className="text-xs text-destructive">Arquivo acima do limite de {MODEL_MAX_UPLOAD_MB} MB.</p>}
        {proprietary && (
          <p role="alert" className="flex items-start gap-1.5 rounded-md border border-amber-300 bg-amber-50 p-2 text-xs text-amber-900 dark:border-amber-800 dark:bg-amber-900/20 dark:text-amber-200">
            <AlertTriangle className="mt-0.5 h-3.5 w-3.5 shrink-0" />{RVT_DWG_INSTRUCTIONS}
          </p>
        )}
      </div>
      {busy && <ProgressBar value={progress} label="Enviando o arquivo" />}
      {error && (
        <p role="alert" data-testid="model-upload-error"
          className={error.disabled3D
            ? "rounded-md border border-amber-300 bg-amber-50 p-2 text-sm text-amber-900 dark:border-amber-800 dark:bg-amber-900/20 dark:text-amber-200"
            : "rounded-md border border-destructive/30 bg-destructive/5 p-2 text-sm text-destructive"}>
          {error.text}
        </p>
      )}
      <div className="flex flex-wrap gap-2">
        <Button type="button" onClick={() => void submit()} disabled={!file || tooBig || proprietary || busy}>
          <Upload className="mr-1.5 h-4 w-4" />{modelId ? "Enviar nova versão" : "Enviar projeto"}
        </Button>
        {onCancel && <Button type="button" variant="ghost" onClick={onCancel} disabled={busy}>Cancelar</Button>}
      </div>
    </div>
  );
}
