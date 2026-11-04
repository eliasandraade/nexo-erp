import { useRef, useState } from "react";
import { ImageIcon, Loader2, Upload, X } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { ApiError } from "@/services/api-client";
import { uploadFile } from "@/services/storage.api";

const ALLOWED = ["image/jpeg", "image/png", "image/webp"];
const MAX_MB = 10;

interface Props {
  context: "build-portal-logo" | "build-portal-cover";
  /** Preview URL of the current image (from the API). */
  previewUrl: string | null;
  hasImage: boolean;
  label: string;
  wide?: boolean;
  /** Receives the storage key to save (null = remove) and a preview URL. */
  onChange: (key: string | null, previewUrl: string | null) => void;
}

/** Uploads a public portal image and hands back the storage KEY (what the portal settings store). */
export function PortalImageUpload({ context, previewUrl, hasImage, label, wide, onChange }: Props) {
  const inputRef = useRef<HTMLInputElement>(null);
  const [loading, setLoading] = useState(false);

  const pick = async (file: File | undefined) => {
    if (inputRef.current) inputRef.current.value = "";
    if (!file) return;
    if (!ALLOWED.includes(file.type)) { toast.error("Formato não permitido. Use JPG, PNG ou WebP."); return; }
    if (file.size === 0) { toast.error("Arquivo vazio não é permitido."); return; }
    if (file.size > MAX_MB * 1024 * 1024) { toast.error(`Arquivo muito grande. Máximo: ${MAX_MB}MB.`); return; }
    setLoading(true);
    try {
      const { key, publicUrl } = await uploadFile(file, context);
      onChange(key, publicUrl);
      toast.success(`${label} enviada. Salve para aplicar.`);
    } catch (e) {
      toast.error(e instanceof ApiError && e.status === 404
        ? "Upload de imagens indisponível neste ambiente."
        : `Falha ao enviar ${label.toLowerCase()}. Tente novamente.`);
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="flex items-center gap-3">
      <div className={`relative shrink-0 overflow-hidden rounded-md border border-border bg-muted ${wide ? "h-16 w-28" : "h-16 w-16"}`}>
        {previewUrl ? (
          <img src={previewUrl} alt={label} className="h-full w-full object-cover" />
        ) : (
          <div className="flex h-full w-full items-center justify-center text-muted-foreground">
            <ImageIcon className="h-5 w-5" />
          </div>
        )}
        {hasImage && (
          <button type="button" onClick={() => onChange(null, null)} title={`Remover ${label.toLowerCase()}`}
            aria-label={`Remover ${label.toLowerCase()}`}
            className="absolute right-0.5 top-0.5 flex h-5 w-5 items-center justify-center rounded-full bg-destructive text-destructive-foreground">
            <X className="h-3 w-3" />
          </button>
        )}
      </div>
      <input ref={inputRef} type="file" accept={ALLOWED.join(",")} className="hidden"
        onChange={(e) => void pick(e.target.files?.[0])} />
      <Button type="button" variant="outline" size="sm" disabled={loading} onClick={() => inputRef.current?.click()}>
        {loading ? <Loader2 className="mr-2 h-4 w-4 animate-spin" /> : <Upload className="mr-2 h-4 w-4" />}
        {hasImage ? `Trocar ${label.toLowerCase()}` : `Enviar ${label.toLowerCase()}`}
      </Button>
    </div>
  );
}
