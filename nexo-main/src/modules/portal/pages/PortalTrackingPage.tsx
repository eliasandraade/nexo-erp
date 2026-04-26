import { useParams, Link } from "react-router-dom";
import { RefreshCw, CheckCircle2, XCircle, Clock, Package, Truck, AlertCircle } from "lucide-react";
import { cn } from "@/lib/utils";
import { useOrderTracking } from "../hooks/useOrderTracking";

const STATUS_STEPS = [
  "Received",
  "Accepted",
  "InPreparation",
  "ReadyForPickup",
  "OutForDelivery",
  "Delivered",
];

const STATUS_ICON: Record<string, React.ElementType> = {
  Received:       Clock,
  Accepted:       CheckCircle2,
  InPreparation:  Package,
  ReadyForPickup: Package,
  OutForDelivery: Truck,
  Delivered:      CheckCircle2,
  Rejected:       XCircle,
  Cancelled:      XCircle,
};

const STATUS_COLOR: Record<string, string> = {
  Received:       "text-amber-400",
  Accepted:       "text-blue-400",
  InPreparation:  "text-amber-400",
  ReadyForPickup: "text-green-400",
  OutForDelivery: "text-purple-400",
  Delivered:      "text-green-500",
  Rejected:       "text-red-400",
  Cancelled:      "text-gray-400",
};

export default function PortalTrackingPage() {
  const { token = "" } = useParams<{ token: string }>();
  const { data, isLoading, isError, isFetching, refetch } = useOrderTracking(token);

  const isTerminal = data && ["Delivered", "Rejected", "Cancelled"].includes(data.status);
  const stepIndex  = data ? STATUS_STEPS.indexOf(data.status) : -1;
  const Icon       = data ? (STATUS_ICON[data.status] ?? Clock) : Clock;

  return (
    <div className="min-h-screen bg-background flex flex-col items-center justify-start p-6 pt-16">
      <div className="w-full max-w-sm flex flex-col gap-6">

        {isLoading && (
          <div className="flex flex-col items-center gap-3 py-12">
            <div className="w-8 h-8 border-2 border-primary border-t-transparent rounded-full animate-spin" />
            <p className="text-sm text-muted-foreground">Rastreando pedido...</p>
          </div>
        )}

        {isError && (
          <div className="flex flex-col items-center gap-3 text-center">
            <AlertCircle className="h-12 w-12 text-muted-foreground" />
            <p className="font-semibold">Pedido não encontrado</p>
            <p className="text-sm text-muted-foreground">
              O link pode estar incorreto ou o pedido expirou.
            </p>
          </div>
        )}

        {data && (
          <>
            {/* Order number + status */}
            <div className="flex flex-col items-center gap-2 text-center">
              <div className={cn("p-4 rounded-full bg-muted", STATUS_COLOR[data.status])}>
                <Icon className="h-8 w-8" />
              </div>
              <p className="text-xs text-muted-foreground">Pedido #{data.orderNumber}</p>
              <h1 className="text-xl font-bold">{data.statusLabel}</h1>
              {data.estimatedMinutes && !isTerminal && (
                <p className="text-sm text-muted-foreground">
                  Tempo estimado: ~{data.estimatedMinutes} min
                </p>
              )}
            </div>

            {/* Progress bar (delivery only) */}
            {data.orderType === "Delivery" && !["Rejected", "Cancelled"].includes(data.status) && (
              <div className="flex items-center gap-1">
                {STATUS_STEPS.slice(0, -1).map((s, i) => (
                  <div
                    key={s}
                    className={cn(
                      "h-1.5 flex-1 rounded-full transition-colors",
                      i <= stepIndex ? "bg-primary" : "bg-muted"
                    )}
                  />
                ))}
              </div>
            )}

            {/* Refresh */}
            {!isTerminal && (
              <button
                onClick={() => refetch()}
                disabled={isFetching}
                className="flex items-center gap-2 text-sm text-muted-foreground hover:text-foreground mx-auto transition-colors"
              >
                <RefreshCw className={cn("h-4 w-4", isFetching && "animate-spin")} />
                Atualizar
              </button>
            )}

            {isTerminal && (
              <p className="text-xs text-muted-foreground text-center">
                Status final — atualizações automáticas pausadas.
              </p>
            )}
          </>
        )}

        <Link
          to="/"
          className="text-sm text-muted-foreground hover:text-foreground text-center transition-colors"
        >
          ← Voltar ao início
        </Link>
      </div>
    </div>
  );
}
