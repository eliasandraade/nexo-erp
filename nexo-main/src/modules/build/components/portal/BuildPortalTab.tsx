import { Info } from "lucide-react";
import { usePortalCompany, useProjectPortal } from "../../hooks/use-build-portal";
import { PortalCompanySection } from "./PortalCompanySection";
import { ProjectPortalSettings } from "./ProjectPortalSettings";
import { ClientUpdatesSection } from "./ClientUpdatesSection";

/** "Portal do cliente": company identity, this project's portal (what the client sees), publication and updates. */
export function BuildPortalTab({ projectId }: { projectId: string }) {
  const { data: portal, isLoading, isError } = useProjectPortal(projectId);
  const { data: company } = usePortalCompany();

  return (
    <div className="space-y-5">
      <div className="flex items-start gap-2 rounded-md border border-border bg-muted/30 p-3 text-xs text-muted-foreground">
        <Info className="mt-0.5 h-3.5 w-3.5 shrink-0" />
        <p>
          O portal do cliente é uma página pública da obra, no site da Orken, onde o seu cliente acompanha o andamento.
          Você decide exatamente o que aparece; nada interno (notas do diário, custos detalhados) é mostrado.
        </p>
      </div>

      {/* the company address comes first until it exists: no obra can be published without it */}
      {company && !company.configured && <PortalCompanySection />}

      {isLoading ? (
        <div className="h-40 rounded-md bg-muted animate-pulse" />
      ) : isError || !portal ? (
        <p className="rounded-md border border-destructive/20 bg-destructive/5 p-6 text-center text-sm text-destructive">
          Erro ao carregar o portal desta obra.
        </p>
      ) : (
        <ProjectPortalSettings projectId={projectId} portal={portal} company={company} />
      )}

      <ClientUpdatesSection projectId={projectId} />
      {company?.configured && <PortalCompanySection />}
    </div>
  );
}
