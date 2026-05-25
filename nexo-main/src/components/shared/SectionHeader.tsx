import { cn } from "@/lib/utils";

interface SectionHeaderProps {
  title: string;
  description?: string;
  actions?: React.ReactNode;
  className?: string;
}

export function SectionHeader({ title, description, actions, className }: SectionHeaderProps) {
  return (
    <div className={cn("flex items-center justify-between gap-4", className)}>
      <div className="min-w-0">
        <h2 className="text-[13px] font-semibold text-foreground leading-none">{title}</h2>
        {description && (
          <p className="text-[11.5px] text-muted-foreground mt-0.5">{description}</p>
        )}
      </div>
      {actions && (
        <div className="flex items-center gap-2 shrink-0">{actions}</div>
      )}
    </div>
  );
}
