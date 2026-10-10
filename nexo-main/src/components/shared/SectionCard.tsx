import { cn } from "@/lib/utils";

interface SectionCardProps {
  title?: string;
  description?: string;
  children: React.ReactNode;
  className?: string;
  actions?: React.ReactNode;
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
        "data-region",
        !noPadding && "p-5",
        className
      )}
    >
      {(title || actions) && (
        <div className={cn(
          "flex flex-wrap items-center justify-between gap-3",
          noPadding ? "px-5 pt-4 pb-3" : "mb-4"
        )}>
          <div>
            {title && (
              <h3 className="section-heading text-foreground">{title}</h3>
            )}
            {description && (
              <p className="text-sm text-muted-foreground mt-0.5">{description}</p>
            )}
          </div>
          {actions && <div className="flex items-center gap-2">{actions}</div>}
        </div>
      )}
      {noPadding && (title || actions)
        ? <div className="border-t border-border">{children}</div>
        : children
      }
    </div>
  );
}
