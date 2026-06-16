import { useNavigate } from "react-router-dom";
import { Check, ChevronsUpDown } from "lucide-react";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { useWorkspace } from "@/modules/workspace/WorkspaceContext";
import type { WorkspaceId } from "@/modules/workspace/types";

/**
 * Shows the active workspace and — when the tenant has more than one — lets the
 * user switch between them. A single-workspace tenant sees a static label so the
 * sidebar still announces which Orken area they're in.
 */
export function WorkspaceSwitcher({ onNav }: { onNav?: () => void }) {
  const { available, active, setActive } = useWorkspace();
  const navigate = useNavigate();

  if (!active) return null;
  const ActiveIcon = active.icon;

  function choose(id: WorkspaceId, home: string) {
    setActive(id);
    onNav?.();
    navigate(home, { replace: true });
  }

  // Single workspace → static identity chip (no dropdown).
  if (available.length < 2) {
    return (
      <div className="mx-3 mb-3 flex items-center gap-2.5 rounded-lg border border-sidebar-border/70 px-2.5 py-2">
        <span
          className="flex h-6 w-6 shrink-0 items-center justify-center rounded-md"
          style={{ background: `${active.accent}26`, color: active.accent }}
        >
          <ActiveIcon className="h-[14px] w-[14px]" strokeWidth={2} />
        </span>
        <span className="truncate text-[12.5px] font-semibold text-white">{active.name}</span>
      </div>
    );
  }

  return (
    <div className="mx-3 mb-3">
      <DropdownMenu>
        <DropdownMenuTrigger asChild>
          <button className="flex w-full items-center gap-2.5 rounded-lg border border-sidebar-border/70 px-2.5 py-2 text-left transition-colors hover:bg-sidebar-accent">
            <span
              className="flex h-6 w-6 shrink-0 items-center justify-center rounded-md"
              style={{ background: `${active.accent}26`, color: active.accent }}
            >
              <ActiveIcon className="h-[14px] w-[14px]" strokeWidth={2} />
            </span>
            <div className="min-w-0 flex-1">
              <p className="truncate text-[12.5px] font-semibold leading-tight text-white">
                {active.name}
              </p>
              <p className="text-[10px] leading-tight text-sidebar-muted">Área de trabalho</p>
            </div>
            <ChevronsUpDown className="h-3.5 w-3.5 shrink-0 text-sidebar-muted" />
          </button>
        </DropdownMenuTrigger>

        <DropdownMenuContent align="start" className="w-56">
          <DropdownMenuLabel className="text-[11px] font-medium uppercase tracking-[0.1em] text-muted-foreground">
            Trocar de área
          </DropdownMenuLabel>
          <DropdownMenuSeparator />
          {available.map((ws) => {
            const Icon = ws.icon;
            const isActive = ws.id === active.id;
            return (
              <DropdownMenuItem
                key={ws.id}
                onClick={() => choose(ws.id, ws.home)}
                className="cursor-pointer gap-2.5"
              >
                <span
                  className="flex h-6 w-6 shrink-0 items-center justify-center rounded-md"
                  style={{ background: `${ws.accent}1f`, color: ws.accent }}
                >
                  <Icon className="h-[14px] w-[14px]" strokeWidth={2} />
                </span>
                <span className="flex-1 text-[13px] font-medium">{ws.name}</span>
                {isActive && <Check className="h-3.5 w-3.5 text-primary" />}
              </DropdownMenuItem>
            );
          })}
        </DropdownMenuContent>
      </DropdownMenu>
    </div>
  );
}
