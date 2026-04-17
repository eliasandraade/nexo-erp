import { cn } from "@/lib/utils";
import type { ConnectionMode } from "../types";

export function KitchenConnectionBadge({ mode }: { mode: ConnectionMode }) {
  return (
    <div className="flex items-center gap-1.5">
      <span
        className={cn(
          "h-2 w-2 rounded-full",
          mode === "realtime" ? "bg-green-500 animate-pulse" : "bg-amber-400"
        )}
      />
      <span className="text-xs text-gray-400">
        {mode === "realtime" ? "Tempo real" : "Atualizando (10s)"}
      </span>
    </div>
  );
}
