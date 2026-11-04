import { useState } from "react";
import { CheckCircle2, Circle, Copy, ExternalLink, Globe } from "lucide-react";
import { toast } from "sonner";
import { cn } from "@/lib/utils";
import { Button } from "@/components/ui/button";
import { usePublishPortal, useUnpublishPortal } from "../../hooks/use-build-portal";
import { PORTAL_PUBLIC_ORIGIN, type BuildPortalCompanyDto, type BuildProjectPortalDto } from "../../api/build-portal.api";
import { portalPublicUrl } from "../../lib/portal";
import { fmtDateTime } from "../../lib/format";

interface Props {
  projectId: string;
  portal:    BuildProjectPortalDto;
  company:   BuildPortalCompanyDto | undefined;
  /** The form has changes not yet saved (publishing uses what is saved). */
  dirty:     boolean;
}

/** Publish / unpublish with a checklist of what is missing; shows the server's reason when it refuses. */
export function PortalPublishPanel({ projectId, portal, company, dirty }: Props) {
  const publishMut   = usePublishPortal(projectId);
  const unpublishMut = useUnpublishPortal(projectId);
  const [error, setError] = useState<string | null>(null);

  const url = portal.publicPath
    ? `${PORTAL_PUBLIC_ORIGIN}${portal.publicPath}`
    : portalPublicUrl(company?.slug ?? portal.companySlug, portal.slug);

  const checklist = [
    { ok: !!company?.configured && !!company.slug, label: "Endereço da empresa configurado" },
    { ok: portal.exists && !!portal.slug,           label: "Endereço da obra salvo" },
    ...(portal.access === "Password" ? [{ ok: portal.hasPassword, label: "Senha do portal definida" }] : []),
    { ok: !dirty,                                   label: "Alterações salvas" },
  ];
  const ready = checklist.every((c) => c.ok);

  const publish = () => {
    setError(null);
    publishMut.mutate(undefined, {
      onSuccess: () => toast.success("Portal publicado."),
      onError:   (e) => setError(e instanceof Error ? e.message : "Não foi possível publicar."),
    });
  };

  const unpublish = () => {
    if (!confirm("Despublicar o portal? O cliente deixa de ter acesso até você publicar de novo.")) return;
    setError(null);
    unpublishMut.mutate(undefined, {
      onSuccess: () => toast.success("Portal despublicado."),
      onError:   (e) => setError(e instanceof Error ? e.message : "Não foi possível despublicar."),
    });
  };

  const copy = () => {
    void navigator.clipboard?.writeText(url).then(
      () => toast.success("Link copiado."),
      () => toast.error("Não foi possível copiar."),
    );
  };

  return (
    <section className="space-y-3 rounded-md border border-border bg-card p-4">
      <div className="flex flex-wrap items-start justify-between gap-2">
        <div>
          <h3 className="flex items-center gap-1.5 text-sm font-semibold">
            <Globe className="h-4 w-4 text-muted-foreground" /> Publicação
          </h3>
          <p className="text-xs text-muted-foreground">
            {portal.isPublished
              ? `Publicado${portal.publishedAt ? ` desde ${fmtDateTime(portal.publishedAt)}` : ""} · ${portal.access === "Password" ? "com senha" : "público"}`
              : "Não publicado — o cliente ainda não tem acesso."}
          </p>
        </div>
        <span className={cn("rounded-full px-2 py-0.5 text-xs font-medium",
          portal.isPublished ? "bg-emerald-100 text-emerald-700 dark:bg-emerald-900/30 dark:text-emerald-400" : "bg-muted text-muted-foreground")}>
          {portal.isPublished ? "Publicado" : "Rascunho"}
        </span>
      </div>

      <div className="flex flex-wrap items-center gap-2 rounded-md border border-border bg-muted/30 p-2">
        <code className="min-w-0 flex-1 truncate text-xs" data-testid="portal-public-url">{url}</code>
        <Button type="button" size="sm" variant="ghost" className="h-7 px-2" onClick={copy} title="Copiar link" aria-label="Copiar link">
          <Copy className="h-3.5 w-3.5" />
        </Button>
        {portal.isPublished && portal.publicPath && (
          <a href={url} target="_blank" rel="noopener noreferrer" title="Abrir portal" aria-label="Abrir portal"
            className="p-1.5 text-muted-foreground hover:text-foreground">
            <ExternalLink className="h-3.5 w-3.5" />
          </a>
        )}
      </div>

      {!portal.isPublished && (
        <ul className="space-y-1" aria-label="O que falta para publicar">
          {checklist.map((c) => (
            <li key={c.label} className={cn("flex items-center gap-1.5 text-xs", c.ok ? "text-foreground" : "text-muted-foreground")}>
              {c.ok
                ? <CheckCircle2 className="h-3.5 w-3.5 text-emerald-600" />
                : <Circle className="h-3.5 w-3.5" />}
              {c.label}
            </li>
          ))}
        </ul>
      )}

      {error && (
        <p role="alert" className="rounded-md border border-destructive/30 bg-destructive/5 p-2 text-xs text-destructive">{error}</p>
      )}

      <div className="flex justify-end gap-2">
        {portal.isPublished ? (
          <Button size="sm" variant="outline" onClick={unpublish} disabled={unpublishMut.isPending}>Despublicar</Button>
        ) : (
          <Button size="sm" onClick={publish} disabled={publishMut.isPending || dirty}
            title={dirty ? "Salve as alterações antes de publicar" : ready ? undefined : "Veja o que falta acima"}>
            {publishMut.isPending ? "Publicando…" : "Publicar portal"}
          </Button>
        )}
      </div>
    </section>
  );
}
