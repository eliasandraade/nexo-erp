import { useRef, useState } from "react";
import { Loader2, Upload, X } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { uploadFile, type StorageContext } from "@/services/storage.api";

interface Props {
  context: StorageContext;
  value: string | null | undefined;
  onChange: (url: string | null) => void;
  label?: string;
  accept?: string;
  maxMb?: number;
}

const ALLOWED_TYPES = ["image/jpeg", "image/png", "image/webp"];

export function ImageUploadButton({
  context,
  value,
  onChange,
  label = "Imagem",
  accept = "image/jpeg,image/png,image/webp",
  maxMb = 10,
}: Props) {
  const [loading, setLoading] = useState(false);
  const inputRef = useRef<HTMLInputElement>(null);

  const handleFileChange = async (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    // Reset input so the same file can be reselected after removal
    if (inputRef.current) inputRef.current.value = "";
    if (!file) return;

    if (!ALLOWED_TYPES.includes(file.type)) {
      toast.error("Formato não permitido. Use JPG, PNG ou WebP.");
      return;
    }
    if (file.size > maxMb * 1024 * 1024) {
      toast.error(`Arquivo muito grande. Máximo: ${maxMb}MB.`);
      return;
    }
    if (file.size === 0) {
      toast.error("Arquivo vazio não é permitido.");
      return;
    }

    setLoading(true);
    try {
      const result = await uploadFile(file, context);
      onChange(result.publicUrl);
      toast.success(`${label} enviada com sucesso.`);
    } catch {
      toast.error(`Falha ao enviar ${label.toLowerCase()}. Tente novamente.`);
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="space-y-2">
      {value && (
        <div className="relative inline-block">
          <img
            src={value}
            alt={label}
            className="h-24 w-24 rounded-md object-cover border border-border"
          />
          <button
            type="button"
            onClick={() => onChange(null)}
            className="absolute -top-1.5 -right-1.5 rounded-full bg-destructive text-destructive-foreground h-5 w-5 flex items-center justify-center"
            title={`Remover ${label.toLowerCase()}`}
          >
            <X className="h-3 w-3" />
          </button>
        </div>
      )}
      <input
        ref={inputRef}
        type="file"
        accept={accept}
        className="hidden"
        onChange={handleFileChange}
      />
      <Button
        type="button"
        variant="outline"
        size="sm"
        disabled={loading}
        onClick={() => inputRef.current?.click()}
      >
        {loading
          ? <Loader2 className="mr-2 h-4 w-4 animate-spin" />
          : <Upload className="mr-2 h-4 w-4" />}
        {value ? `Trocar ${label.toLowerCase()}` : `Adicionar ${label.toLowerCase()}`}
      </Button>
    </div>
  );
}
