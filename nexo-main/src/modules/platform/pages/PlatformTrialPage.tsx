import { useNavigate } from "react-router-dom";
import { AlertTriangle, RefreshCw, ExternalLink } from "lucide-react";
import { cn } from "@/lib/utils";
import { useTrialExpired } from "../hooks/usePlatformTenants";

export default function PlatformTrialPage() {
  const navigate = useNavigate();
  const { data = [], isLoading, refetch, isFetching } = useTrialExpired();

  return (
    <div className="p-6 space-y-5 max-w-4xl">

      {/* Header */}
      <div className="flex items-center justify-between">
        <div>
          <h1 className="font-display text-[20px] font-bold text-foreground tracking-tight">Trial Expirado</h1>
          <p className="text-sm text-muted-foreground mt-0.5">
            Clientes com período de trial ou assinatura vencida
          </p>
        </div>
        <button
          onClick={() => refetch()}
          disabled={isFetching}
          className="flex items-center gap-1.5 h-8 px-3 rounded-xl border border-border text-sm text-muted-foreground hover:text-foreground hover:bg-muted transition-colors disabled:opacity-50"
        >
          <RefreshCw className={cn("h-3.5 w-3.5", isFetching && "animate-spin")} />
          Atualizar
        </button>
      </div>

      {/* Summary badge */}
      {!isLoading && data.length > 0 && (
        <div className="flex items-center gap-2 px-4 py-3 bg-amber-500/10 border border-amber-500/30 rounded-lg">
          <AlertTriangle className="h-4 w-4 text-amber-600 shrink-0" />
          <p className="text-sm text-amber-700">
            <span className="font-semibold">{data.length} cliente{data.length !== 1 ? "s" : ""}</span>{" "}
            com trial ou assinatura expirada. Considere entrar em contato.
          </p>
        </div>
      )}

      {/* List */}
      <div className="bg-card border border-border rounded-lg overflow-hidden">
        {isLoading ? (
          <div className="p-10 text-center text-sm text-muted-foreground">Carregando...</div>
        ) : data.length === 0 ? (
          <div className="p-10 text-center">
            <p className="text-sm font-medium text-foreground">Nenhum trial expirado</p>
            <p className="text-xs text-muted-foreground mt-1">Todos os clientes estão dentro do período ativo.</p>
          </div>
        ) : (
          <>
            <div className="px-4 py-2.5 border-b border-border bg-muted/30 grid grid-cols-[1fr_auto_auto_auto] gap-4 text-xs font-medium text-muted-foreground">
              <span>Cliente</span>
              <span>Motivo</span>
              <span>Expirado há</span>
              <span></span>
            </div>
            <div className="divide-y divide-border">
              {data.map((t) => (
                <div key={t.id} className="px-4 py-3.5 grid grid-cols-[1fr_auto_auto_auto] gap-4 items-center hover:bg-muted/20 transition-colors">
                  {/* Company */}
                  <div className="min-w-0">
                    <p className="text-sm font-medium text-foreground truncate">
                      {t.tradeName ?? t.companyName}
                    </p>
                    <p className="text-xs text-muted-foreground truncate">{t.email}</p>
                  </div>

                  {/* Reason */}
                  <span className={cn(
                    "px-2 py-0.5 rounded text-[10px] font-medium whitespace-nowrap",
                    t.expiredReason === "trial"
                      ? "bg-amber-500/10 text-amber-700"
                      : "bg-red-500/10 text-red-700"
                  )}>
                    {t.expiredReason === "trial" ? "Trial" : `Módulo: ${t.expiredReason.replace("subscription:", "")}`}
                  </span>

                  {/* Days ago */}
                  <span className={cn(
                    "text-sm font-semibold tabular-nums whitespace-nowrap",
                    t.expiredDaysAgo > 30 ? "text-destructive" :
                    t.expiredDaysAgo > 7  ? "text-amber-600" :
                    "text-muted-foreground"
                  )}>
                    {t.expiredDaysAgo}d atrás
                  </span>

                  {/* Action */}
                  <button
                    onClick={() => navigate(`/platform/tenants/${t.id}`)}
                    className="flex items-center gap-1 h-7 px-2.5 rounded-md border border-border text-xs text-muted-foreground hover:text-foreground hover:bg-muted transition-colors"
                  >
                    <ExternalLink className="h-3 w-3" /> Ver
                  </button>
                </div>
              ))}
            </div>
          </>
        )}
      </div>
    </div>
  );
}
