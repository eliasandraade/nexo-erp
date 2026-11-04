import { useEffect, useState } from "react";
import { Eye, EyeOff } from "lucide-react";
import { toast } from "sonner";
import { cn } from "@/lib/utils";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import { Switch } from "@/components/ui/switch";
import { Textarea } from "@/components/ui/textarea";
import { useSetDailyLogClientVisibility, useSetPhotoClientVisibility } from "../../hooks/use-build-portal";
import type { BuildDailyLogDto, BuildDailyLogPhotoDto } from "../../api/build.api";

interface Props {
  projectId: string;
  log: BuildDailyLogDto;
}

/**
 * Per daily log: whether the client portal shows it, and the summary written for the client.
 * The internal notes never reach the client — only this summary.
 */
export function DailyLogClientVisibility({ projectId, log }: Props) {
  const mut = useSetDailyLogClientVisibility(projectId);
  const savedVisible = !!log.clientVisible;
  const savedSummary = log.clientSummary ?? "";

  const [summary, setSummary] = useState(savedSummary);
  const [editing, setEditing] = useState(false);
  const [needsSummary, setNeedsSummary] = useState(false);

  useEffect(() => { setSummary(log.clientSummary ?? ""); }, [log.clientSummary]);

  const send = (clientVisible: boolean, text: string, done?: () => void) => {
    mut.mutate({ logId: log.id, clientVisible, clientSummary: text.trim() || null }, {
      onSuccess: () => {
        setNeedsSummary(false);
        toast.success(clientVisible ? "Registro visível ao cliente." : "Registro oculto do cliente.");
        done?.();
      },
      onError: (e) => toast.error(e instanceof Error ? e.message : "Não foi possível mudar a visibilidade."),
    });
  };

  const toggle = (on: boolean) => {
    if (on && !summary.trim()) {   // the server refuses a visible log without a summary: ask for it first
      setEditing(true);
      setNeedsSummary(true);
      return;
    }
    send(on, summary);
  };

  const showEditor = editing || (savedVisible && !savedSummary);
  const dirty = summary.trim() !== savedSummary.trim();
  const switchId = `log-client-visible-${log.id}`;
  const summaryId = `log-client-summary-${log.id}`;

  return (
    <div className={cn("space-y-2 rounded-md border p-2.5", savedVisible ? "border-primary/30 bg-primary/5" : "border-border bg-muted/20")}>
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="flex items-center gap-2">
          <Switch id={switchId} checked={savedVisible} disabled={mut.isPending} onCheckedChange={toggle} />
          <Label htmlFor={switchId} className="flex items-center gap-1 text-xs font-medium">
            {savedVisible ? <Eye className="h-3.5 w-3.5" /> : <EyeOff className="h-3.5 w-3.5 text-muted-foreground" />}
            Visível ao cliente
          </Label>
        </div>
        {!showEditor && (
          <button type="button" onClick={() => setEditing(true)} className="text-xs text-primary hover:underline">
            {savedSummary ? "Editar resumo" : "Escrever resumo para o cliente"}
          </button>
        )}
      </div>

      {!showEditor && savedSummary && (
        <p className="text-xs text-muted-foreground"><span className="font-medium">Resumo para o cliente:</span> {savedSummary}</p>
      )}

      {showEditor && (
        <div className="space-y-1.5">
          <Label htmlFor={summaryId} className="text-xs">Resumo para o cliente</Label>
          <Textarea id={summaryId} value={summary} maxLength={2000} onChange={(e) => setSummary(e.target.value)}
            placeholder="Ex: Hoje concluímos a concretagem da laje do 1º pavimento."
            className="min-h-[60px] resize-none text-sm" />
          <p className="text-[11px] text-muted-foreground">
            O cliente nunca vê as notas internas do diário — só este resumo.
          </p>
          {needsSummary && !summary.trim() && (
            <p role="alert" className="text-xs text-destructive">
              Escreva o resumo para o cliente antes de mostrar este registro no portal.
            </p>
          )}
          <div className="flex gap-2">
            <Button size="sm" className="h-7 text-xs" disabled={mut.isPending || !summary.trim() || (!dirty && !needsSummary)}
              onClick={() => send(needsSummary ? true : savedVisible, summary, () => setEditing(false))}>
              {needsSummary ? "Salvar e mostrar ao cliente" : "Salvar resumo"}
            </Button>
            <Button size="sm" variant="ghost" className="h-7 text-xs"
              onClick={() => { setEditing(false); setNeedsSummary(false); setSummary(savedSummary); }}>
              Cancelar
            </Button>
          </div>
        </div>
      )}
    </div>
  );
}

/** Per photo: shown on the client portal (only when its daily log is visible too). */
export function PhotoClientVisibilityToggle({ projectId, photo, logVisible }: {
  projectId: string;
  photo: BuildDailyLogPhotoDto;
  logVisible: boolean;
}) {
  const mut = useSetPhotoClientVisibility(projectId);
  const visible = !!photo.clientVisible;
  const label = visible ? "Foto visível ao cliente" : "Foto oculta do cliente";
  return (
    <button
      type="button"
      aria-pressed={visible}
      aria-label={label}
      title={visible
        ? logVisible ? "Visível ao cliente — clique para ocultar" : "Marcada como visível, mas só aparece quando o registro do dia estiver visível"
        : "Oculta do cliente — clique para mostrar"}
      disabled={mut.isPending}
      onClick={() => mut.mutate({ photoId: photo.id, clientVisible: !visible }, {
        onError: (e) => toast.error(e instanceof Error ? e.message : "Não foi possível mudar a foto."),
      })}
      className={cn(
        "absolute bottom-0.5 left-0.5 flex h-5 w-5 items-center justify-center rounded-full border shadow-sm transition-colors disabled:opacity-50",
        visible
          ? logVisible ? "border-primary bg-primary text-primary-foreground" : "border-amber-500 bg-amber-100 text-amber-700"
          : "border-border bg-background/90 text-muted-foreground",
      )}
    >
      {visible ? <Eye className="h-3 w-3" /> : <EyeOff className="h-3 w-3" />}
    </button>
  );
}
