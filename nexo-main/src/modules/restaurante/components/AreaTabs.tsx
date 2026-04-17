import { cn } from "@/lib/utils";
import type { AreaDto } from "../types";

interface AreaTabsProps {
  areas: AreaDto[];
  activeAreaId: string | null;
  onSelect: (areaId: string | null) => void;
}

export function AreaTabs({ areas, activeAreaId, onSelect }: AreaTabsProps) {
  return (
    <div className="flex gap-2 overflow-x-auto pb-1 scrollbar-none">
      <button
        onClick={() => onSelect(null)}
        className={cn(
          "shrink-0 rounded-full px-4 py-1.5 text-sm font-medium transition-colors",
          activeAreaId === null
            ? "bg-primary text-primary-foreground"
            : "bg-muted text-muted-foreground hover:bg-muted/80"
        )}
      >
        Todas
      </button>
      {areas.map((area) => (
        <button
          key={area.id}
          onClick={() => onSelect(area.id)}
          className={cn(
            "shrink-0 rounded-full px-4 py-1.5 text-sm font-medium transition-colors",
            activeAreaId === area.id
              ? "bg-primary text-primary-foreground"
              : "bg-muted text-muted-foreground hover:bg-muted/80"
          )}
        >
          {area.name}
        </button>
      ))}
    </div>
  );
}
