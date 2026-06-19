import { useEffect, useRef, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { AlertCircle, Check, Copy, ExternalLink, Globe, Loader2, X } from "lucide-react";
import { Button } from "@/components/ui/button";
import { cn } from "@/lib/utils";
import { setPublicSlug, checkSlugAvailability } from "@/modules/stores/services/storesApi";

const PORTAL_BASE = "app.orken.com.br";
const BOOKING_PREFIX = "agendar";

/** Preview-only normalization; the backend is authoritative (Store.NormalizeSlug). */
export function normalizeSlug(raw: string): string {
  return raw
    .toLowerCase()
    .normalize("NFD")
    .replace(/[̀-ͯ]/g, "")
    .replace(/[^a-z0-9\s-]/g, "")
    .trim()
    .replace(/[\s_]+/g, "-")
    .replace(/-{2,}/g, "-");
}

export function publicBookingUrl(slug: string): string {
  return `${PORTAL_BASE}/${BOOKING_PREFIX}/${slug}`;
}

interface PortalSlugSectionProps {
  storeId: string;
  currentSlug: string | null;
}

export function PortalSlugSection({ storeId, currentSlug }: PortalSlugSectionProps) {
  const qc = useQueryClient();
  const [slugInput, setSlugInput] = useState(currentSlug ?? "");
  const [copied, setCopied] = useState(false);

  useEffect(() => { if (currentSlug) setSlugInput(currentSlug); }, [currentSlug]);

  const previewSlug = normalizeSlug(slugInput);
  const hasSlug = previewSlug.length >= 3;
  const url = publicBookingUrl(previewSlug);
  const isSavedSlug = previewSlug === currentSlug;

  // Debounced availability check.
  const [debounced, setDebounced] = useState("");
  const timer = useRef<ReturnType<typeof setTimeout> | null>(null);
  useEffect(() => {
    if (timer.current) clearTimeout(timer.current);
    timer.current = setTimeout(() => setDebounced(previewSlug), 500);
    return () => { if (timer.current) clearTimeout(timer.current); };
  }, [previewSlug]);

  const { data: slugCheck, isFetching: checking } = useQuery({
    queryKey: ["service-slug-check", debounced, storeId],
    queryFn: () => checkSlugAvailability(debounced, storeId || undefined),
    enabled: hasSlug && debounced.length >= 3 && debounced === previewSlug && !isSavedSlug,
    staleTime: 10_000,
  });

  const saveMut = useMutation({
    mutationFn: () => setPublicSlug(storeId, previewSlug || null),
    onSuccess: () => qc.invalidateQueries({ queryKey: ["stores", "mine"] }),
  });

  const taken = slugCheck?.available === false;
  const available = slugCheck?.available === true;

  function copy() {
    navigator.clipboard.writeText(`https://${url}`).then(() => {
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    });
  }

  return (
    <div className="space-y-3">
      <div className="flex gap-2">
        <div className={cn(
          "flex flex-1 items-center overflow-hidden rounded-lg border bg-muted/40 transition-all focus-within:ring-1",
          !isSavedSlug && debounced === previewSlug && taken
            ? "border-destructive focus-within:border-destructive focus-within:ring-destructive"
            : "border-border focus-within:border-primary focus-within:ring-primary",
        )}>
          <span className="shrink-0 select-none whitespace-nowrap pl-3 pr-1 text-xs text-muted-foreground">
            {PORTAL_BASE}/{BOOKING_PREFIX}/
          </span>
          <input
            value={slugInput}
            onChange={(e) => setSlugInput(e.target.value)}
            placeholder="minha-clinica"
            className="flex-1 bg-transparent py-2 text-sm outline-none placeholder:text-muted-foreground/50"
          />
          <span className="shrink-0 pr-2">
            {hasSlug && checking && <Loader2 className="h-3.5 w-3.5 animate-spin text-muted-foreground" />}
            {hasSlug && !checking && (isSavedSlug || available) && <Check className="h-3.5 w-3.5 text-primary" />}
            {hasSlug && !checking && !isSavedSlug && taken && <X className="h-3.5 w-3.5 text-destructive" />}
          </span>
        </div>
        <Button
          size="sm"
          className="shrink-0"
          disabled={!hasSlug || saveMut.isPending || checking || isSavedSlug || taken}
          onClick={() => saveMut.mutate()}
        >
          {saveMut.isPending ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : "Salvar"}
        </Button>
      </div>

      {hasSlug && !checking && !isSavedSlug && debounced === previewSlug && taken && (
        <p className="flex items-center gap-2 text-xs text-destructive">
          <AlertCircle className="h-3.5 w-3.5 shrink-0" />
          {slugCheck?.reason ?? "Este endereço já está em uso. Escolha outro."}
        </p>
      )}

      {hasSlug ? (
        <div className="flex items-center gap-3 rounded-xl border border-border bg-card p-3">
          <Globe className="h-4 w-4 shrink-0 text-primary" />
          <div className="min-w-0 flex-1">
            <p className="text-xs text-muted-foreground">Link público de agendamento</p>
            <p className="truncate text-sm font-medium">{url}</p>
          </div>
          <div className="flex shrink-0 items-center gap-1">
            <button onClick={copy} title="Copiar link"
              className="rounded-md p-1.5 text-muted-foreground transition-colors hover:bg-muted hover:text-foreground">
              {copied ? <Check className="h-3.5 w-3.5 text-primary" /> : <Copy className="h-3.5 w-3.5" />}
            </button>
            {isSavedSlug && (
              <a href={`https://${url}`} target="_blank" rel="noopener noreferrer" title="Abrir portal"
                className="rounded-md p-1.5 text-muted-foreground transition-colors hover:bg-muted hover:text-foreground">
                <ExternalLink className="h-3.5 w-3.5" />
              </a>
            )}
          </div>
        </div>
      ) : (
        <div className="flex items-start gap-2 rounded-lg border border-amber-700/30 bg-amber-950/30 px-3 py-2 text-xs text-amber-400/90">
          <AlertCircle className="mt-0.5 h-3.5 w-3.5 shrink-0" />
          Defina um endereço (mínimo 3 caracteres) para ativar o portal. Sem ele não há link público.
        </div>
      )}
    </div>
  );
}
