import { cn } from "@/lib/utils";

interface PageHeaderProps {
  title: string;
  description?: string;
  actions?: React.ReactNode;
  className?: string;
  /** Optional eyebrow label above the title */
  eyebrow?: string;
}

export function PageHeader({ title, description, actions, className, eyebrow }: PageHeaderProps) {
  return (
    <div className={cn("flex items-start justify-between gap-4", className)}>
      <div className="flex items-start gap-3 min-w-0">
        {/* Indigo accent line */}
        <div className="w-0.5 rounded-full bg-[#5B4DFF] self-stretch min-h-[28px] shrink-0 mt-0.5" />

        <div className="min-w-0">
          {eyebrow && (
            <p className="text-[10px] font-semibold uppercase tracking-[0.1em] text-[#5B4DFF] mb-1">
              {eyebrow}
            </p>
          )}
          <h1 className="font-display text-[22px] font-bold text-foreground leading-tight tracking-tight">
            {title}
          </h1>
          {description && (
            <p className="text-[13px] text-muted-foreground mt-0.5 leading-snug">
              {description}
            </p>
          )}
        </div>
      </div>

      {actions && (
        <div className="flex items-center gap-2 shrink-0">
          {actions}
        </div>
      )}
    </div>
  );
}
