import { cn } from "@/lib/utils";

interface PageHeaderProps {
  title: string;
  description?: string;
  actions?: React.ReactNode;
  className?: string;
  eyebrow?: string;
}

export function PageHeader({ title, description, actions, className, eyebrow }: PageHeaderProps) {
  return (
    <div className={cn("flex items-start justify-between gap-4", className)}>
      <div className="flex items-start gap-3 min-w-0">
        {/* Brand accent rule */}
        <div className="w-[3px] rounded-full bg-primary self-stretch min-h-[24px] shrink-0 mt-0.5" />

        <div className="min-w-0">
          {eyebrow && (
            <p className="text-[10px] font-semibold uppercase tracking-[0.12em] text-primary mb-1">
              {eyebrow}
            </p>
          )}
          <h1 className="font-display text-[20px] font-bold text-foreground leading-tight tracking-tight">
            {title}
          </h1>
          {description && (
            <p className="text-[12.5px] text-muted-foreground mt-1 leading-snug">
              {description}
            </p>
          )}
        </div>
      </div>

      {actions && (
        <div className="flex items-center gap-2 shrink-0 pt-0.5">
          {actions}
        </div>
      )}
    </div>
  );
}
