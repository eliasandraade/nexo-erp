import { useEffect, useRef, useState } from "react";
import { ImageIcon, Loader2, Upload, X } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Switch } from "@/components/ui/switch";
import { Textarea } from "@/components/ui/textarea";
import {
  Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle,
} from "@/components/ui/dialog";
import { ApiError } from "@/services/api-client";
import { uploadPrivateFile } from "@/services/storage.api";
import { useSaveSelectionOption } from "../../hooks/use-build-commercial";
import type { BuildSelectionOptionDto } from "../../api/build-commercial.api";
import { parseDecimal } from "../../lib/format";
import { errorMessage } from "../commercial/errors";

const ALLOWED = ["image/jpeg", "image/png", "image/webp"];
const MAX_MB = 10;

interface Props {
  projectId: string;
  selectionId: string | null;
  /** null = new option; undefined = closed. */
  option: BuildSelectionOptionDto | null | undefined;
  onClose: () => void;
}

/** Option of a client selection: price delta vs. the standard, lead time and a private image. */
export function SelectionOptionDialog({ projectId, selectionId, option, onClose }: Props) {
  const open = option !== undefined && !!selectionId;
  const mut = useSaveSelectionOption(projectId);
  const inputRef = useRef<HTMLInputElement>(null);
  const [name, setName] = useState("");
  const [description, setDescription] = useState("");
  const [delta, setDelta] = useState("0");
  const [standard, setStandard] = useState(false);
  const [lead, setLead] = useState("");
  const [imageKey, setImageKey] = useState<string | null>(null);
  const [preview, setPreview] = useState<string | null>(null);
  const [uploading, setUploading] = useState(false);

  useEffect(() => {
    if (!open) return;
    setName(option?.name ?? "");
    setDescription(option?.description ?? "");
    setDelta(String(option?.priceDelta ?? 0));
    setStandard(option?.isStandard ?? false);
    setLead(option?.leadTimeDays != null ? String(option.leadTimeDays) : "");
    setImageKey(option?.imageKey ?? null);
    setPreview(option?.imageUrl ?? null);
  }, [open, option]);

  const pick = async (file: File | undefined) => {
    if (inputRef.current) inputRef.current.value = "";
    if (!file) return;
    if (!ALLOWED.includes(file.type)) { toast.error("Formato não permitido. Use JPG, PNG ou WebP."); return; }
    if (file.size === 0) { toast.error("Arquivo vazio não é permitido."); return; }
    if (file.size > MAX_MB * 1024 * 1024) { toast.error(`Arquivo muito grande. Máximo: ${MAX_MB}MB.`); return; }
    setUploading(true);
    try {
      const { key, url } = await uploadPrivateFile(file, "build-selection-option");
      setImageKey(key);
      setPreview(url);
    } catch (e) {
      toast.error(e instanceof ApiError && e.status === 404
        ? "Upload de imagens indisponível neste ambiente."
        : "Falha ao enviar a imagem. Tente novamente.");
    } finally {
      setUploading(false);
    }
  };

  const save = () => {
    if (!selectionId) return;
    const d = parseDecimal(delta);
    const l = lead.trim() ? Number(lead) : null;
    if (!name.trim()) { toast.error("Informe o nome da opção."); return; }
    if (d == null || Number.isNaN(d)) { toast.error("Diferença de preço inválida."); return; }
    if (l != null && (!Number.isInteger(l) || l < 0)) { toast.error("Prazo adicional deve ser um número inteiro de dias."); return; }
    mut.mutate({
      selectionId,
      optionId: option?.id,
      req: {
        name: name.trim(), description: description.trim() || null, priceDelta: standard ? 0 : d,
        isStandard: standard, imageKey, leadTimeDays: l, order: option?.order ?? null,
      },
    }, {
      onSuccess: () => { toast.success("Opção salva."); onClose(); },
      onError:   (e) => toast.error(errorMessage(e, "Erro ao salvar a opção.")),
    });
  };

  return (
    <Dialog open={open} onOpenChange={(v) => !v && onClose()}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>{option ? "Editar opção" : "Nova opção"}</DialogTitle>
          <DialogDescription>O preço é a diferença em relação à opção padrão (0 = mesmo preço).</DialogDescription>
        </DialogHeader>
        <div className="space-y-3">
          <div>
            <Label htmlFor="so-name" className="text-xs">Nome</Label>
            <Input id="so-name" value={name} onChange={(e) => setName(e.target.value)} className="mt-0.5" />
          </div>
          <div>
            <Label htmlFor="so-desc" className="text-xs">Descrição</Label>
            <Textarea id="so-desc" value={description} onChange={(e) => setDescription(e.target.value)} rows={2} className="mt-0.5" />
          </div>
          <div className="flex items-center justify-between gap-3">
            <Label htmlFor="so-standard" className="text-sm">Opção padrão (incluída no contrato)</Label>
            <Switch id="so-standard" checked={standard} onCheckedChange={setStandard} />
          </div>
          <div className="grid grid-cols-2 gap-3">
            <div>
              <Label htmlFor="so-delta" className="text-xs">Diferença de preço (R$)</Label>
              <Input id="so-delta" value={standard ? "0" : delta} disabled={standard}
                onChange={(e) => setDelta(e.target.value)} inputMode="decimal" className="mt-0.5" />
            </div>
            <div>
              <Label htmlFor="so-lead" className="text-xs">Prazo adicional (dias)</Label>
              <Input id="so-lead" value={lead} onChange={(e) => setLead(e.target.value)} inputMode="numeric" className="mt-0.5" />
            </div>
          </div>
          <div className="flex items-center gap-3">
            <div className="h-16 w-16 shrink-0 overflow-hidden rounded-md border border-border bg-muted">
              {preview ? (
                <img src={preview} alt="Imagem da opção" className="h-full w-full object-cover" />
              ) : (
                <div className="flex h-full w-full items-center justify-center text-muted-foreground">
                  <ImageIcon className="h-5 w-5" />
                </div>
              )}
            </div>
            <input ref={inputRef} type="file" accept={ALLOWED.join(",")} className="hidden" data-testid="option-image-input"
              onChange={(e) => void pick(e.target.files?.[0])} />
            <Button type="button" variant="outline" size="sm" disabled={uploading} onClick={() => inputRef.current?.click()}>
              {uploading ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : <Upload className="mr-2 h-4 w-4" />}
              {imageKey ? "Trocar imagem" : "Enviar imagem"}
            </Button>
            {imageKey && (
              <button type="button" aria-label="Remover imagem" onClick={() => { setImageKey(null); setPreview(null); }}
                className="text-muted-foreground hover:text-destructive"><X className="h-4 w-4" /></button>
            )}
          </div>
          <p className="text-[11px] text-muted-foreground">
            Imagem privada: o cliente só a vê pelo portal, por um link temporário, e só se a escolha estiver visível.
          </p>
        </div>
        <DialogFooter>
          <Button variant="outline" onClick={onClose}>Cancelar</Button>
          <Button onClick={save} disabled={uploading || mut.isPending}>Salvar opção</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
