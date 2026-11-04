import { cn } from "@/lib/utils";
import type { HealthCheck } from "../types";

export function StatusDot({ status }: Readonly<{ status: string }>) {
  let color = "bg-red-500";
  if (status === "healthy") color = "bg-green-500";
  else if (status === "degraded") color = "bg-amber-500";
  else if (status === "not_configured") color = "bg-muted-foreground/40";
  return <span className={`inline-block w-2 h-2 rounded-full ${color} shrink-0`} aria-hidden />;
}

const CHECK_LABELS: Record<string, string> = { database: "Banco de dados", cache: "Cache" };
const CHECK_STATUS: Record<string, string> = {
  healthy:        "Funcionando",
  unhealthy:      "Com falha",
  not_configured: "Não configurado",
};

/** One health check as the server measured it: not_configured is neutral (neither working nor failing). */
export function HealthCheckRow({ check }: Readonly<{ check: HealthCheck }>) {
  const measured = check.status !== "not_configured" && check.latencyMs > 0;
  const latency = check.latencyMs > 1 ? `${check.latencyMs}ms` : "< 1ms";
  return (
    <div className="flex items-center justify-between gap-3">
      <div className="flex items-center gap-2">
        <StatusDot status={check.status} />
        <span className="text-sm text-foreground">{CHECK_LABELS[check.name] ?? check.name}</span>
      </div>
      <span className={cn("text-xs tabular-nums", check.status === "unhealthy" ? "text-red-600" : "text-muted-foreground")}>
        {CHECK_STATUS[check.status] ?? check.status}
        {measured && ` · ${latency}`}
      </span>
    </div>
  );
}
