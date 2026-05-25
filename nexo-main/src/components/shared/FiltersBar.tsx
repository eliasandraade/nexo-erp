import { cn } from "@/lib/utils";

interface FiltersBarProps {
  children: React.ReactNode;
  className?: string;
  actions?: React.ReactNode;
}

export function FiltersBar({ children, className, actions }: FiltersBarProps) {
  return (
    <div className={cn("flex items-center gap-2 flex-wrap", className)}>
      <div className="flex items-center gap-2 flex-1 flex-wrap min-w-0">
        {children}
      </div>
      {actions && (
        <div className="flex items-center gap-2 shrink-0">{actions}</div>
      )}
    </div>
  );
}
