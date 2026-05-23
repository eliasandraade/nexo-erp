import { cn } from "@/lib/utils";

interface SectionCardProps {
  title?: string;
  description?: string;
  children: React.ReactNode;
  className?: string;
  actions?: React.ReactNode;
  /** Remove padding (for tables that need full bleed) */
  noPadding?: boolean;
}

export function SectionCard({
  title,
  description,
  children,
  className,
  actions,
  noPadding = false,
}: SectionCardProps) {
  return (
    <div
      className={cn(
        "bg-card rounded-xl border border-border",
        !noPadding && "p-5",
        className
      )}
    >
      {(title || actions) && (
        <div className={cn("flex items-center justify-between", noPadding ? "px-5 pt-5 pb-4" : "mb-5")}>
          <div>
            {title && (
              <h3 className="text-[13px] font-semibold text-foreground">{title}</h3>
            )}
            {description && (
              <p className="text-[12px] text-muted-foreground mt-0.5">
                {description}
              </p>
            )}
          </div>
          {actions && <div className="flex items-center gap-2">{actions}</div>}
        </div>
      )}
      {noPadding && (title || actions) ? (
        <div className="border-t border-border">{children}</div>
      ) : (
        children
      )}
    </div>
  );
}
