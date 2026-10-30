import { useRef, useState } from "react";
import { Camera, FileUp, Loader2 } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { ApiError } from "@/services/api-client";
import { uploadPrivateFile, type PrivateStorageContext, type PrivateUploadResult } from "@/services/storage.api";

const IMAGES = ["image/jpeg", "image/png", "image/webp"];
const MAX_MB = 10;

interface Props {
  context: PrivateStorageContext;
  /** Also accepts PDF (handover documents). */
  allowPdf?: boolean;
  label: string;
  disabled?: boolean;
  /** Opens the camera on phones (photos only). */
  capture?: boolean;
  onUploaded: (r: PrivateUploadResult, file: File) => void;
  testId?: string;
  size?: "sm" | "default";
}

/** Uploads one private file (never public: the server signs short-lived URLs) and hands back its key. */
export function PrivateUploadButton({ context, allowPdf, label, disabled, capture, onUploaded, testId, size = "sm" }: Props) {
  const inputRef = useRef<HTMLInputElement>(null);
  const [uploading, setUploading] = useState(false);
  const allowed = allowPdf ? [...IMAGES, "application/pdf"] : IMAGES;

  const pick = async (file: File | undefined) => {
    if (inputRef.current) inputRef.current.value = "";
    if (!file) return;
    if (!allowed.includes(file.type)) {
      toast.error(allowPdf ? "Formato não permitido. Use PDF, JPG, PNG ou WebP." : "Formato não permitido. Use JPG, PNG ou WebP.");
      return;
    }
    if (file.size === 0) { toast.error("Arquivo vazio não é permitido."); return; }
    if (file.size > MAX_MB * 1024 * 1024) { toast.error(`Arquivo muito grande. Máximo: ${MAX_MB}MB.`); return; }
    setUploading(true);
    try {
      onUploaded(await uploadPrivateFile(file, context), file);
    } catch (e) {
      toast.error(e instanceof ApiError && e.status === 404
        ? "Upload de arquivos indisponível neste ambiente."
        : "Falha ao enviar o arquivo. Tente novamente.");
    } finally {
      setUploading(false);
    }
  };

  const Icon = allowPdf ? FileUp : Camera;
  return (
    <>
      <input ref={inputRef} type="file" accept={allowed.join(",")} className="hidden" data-testid={testId}
        {...(capture && !allowPdf ? { capture: "environment" as const } : {})}
        onChange={(e) => void pick(e.target.files?.[0])} />
      <Button type="button" variant="outline" size={size} disabled={disabled || uploading}
        onClick={() => inputRef.current?.click()}>
        {uploading ? <Loader2 className="mr-1 h-3.5 w-3.5 animate-spin" /> : <Icon className="mr-1 h-3.5 w-3.5" />}
        {label}
      </Button>
    </>
  );
}

/** Thumbnails of private photos (signed URLs); a missing URL shows a placeholder. */
export function PhotoStrip({ urls, alt }: { urls: string[] | null | undefined; alt: string }) {
  const list = Array.isArray(urls) ? urls : [];
  if (!list.length) return null;
  return (
    <div className="flex flex-wrap gap-2">
      {list.map((u, i) => (
        <a key={`${u}-${i}`} href={u} target="_blank" rel="noreferrer"
          className="block h-16 w-16 overflow-hidden rounded-md border border-border bg-muted">
          <img src={u} alt={`${alt} ${i + 1}`} className="h-full w-full object-cover" />
        </a>
      ))}
    </div>
  );
}
