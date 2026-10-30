import { useSearchParams } from "react-router-dom";
import { leaveImpersonation } from "@/services/api-client";

/**
 * Last screen of an impersonation tab: the tenant access expired or the administrator left it. The tab keeps its
 * (ended) impersonation marker, so it never silently turns into the administrator's own session.
 */
export default function ImpersonationEndedPage() {
  const [params] = useSearchParams();
  const left = params.get("motivo") === "saiu";

  const backToPlatform = () => {
    leaveImpersonation();
    // full load: the tab reads the administrator's own session again
    globalThis.location.assign("/platform");
  };

  return (
    <div className="min-h-screen flex items-center justify-center bg-background p-4">
      <div role="status" className="max-w-sm text-center space-y-3">
        <h1 className="text-lg font-semibold text-foreground">
          {left ? "Acesso encerrado" : "O acesso à empresa terminou"}
        </h1>
        <p className="text-sm text-muted-foreground">
          {left
            ? "Você saiu do acesso à empresa. Esta aba pode ser fechada."
            : "O tempo do acesso da plataforma acabou ou ele foi encerrado. Para continuar, entre de novo pela plataforma."}
        </p>
        <div className="flex items-center justify-center gap-3 pt-1">
          <button
            type="button"
            onClick={() => window.close()}
            className="h-9 px-4 rounded-lg border border-border text-sm hover:bg-muted transition-colors"
          >
            Fechar aba
          </button>
          <button
            type="button"
            onClick={backToPlatform}
            className="h-9 px-4 rounded-lg bg-primary text-primary-foreground text-sm font-medium hover:bg-primary/90 transition-colors"
          >
            Voltar à plataforma
          </button>
        </div>
      </div>
    </div>
  );
}
