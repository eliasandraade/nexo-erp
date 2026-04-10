import { cn } from "@/lib/utils";

interface SectionCardProps {
  title?: string;
  description?: string;
  children: React.ReactNode;
  className?: string;
  actions?: React.ReactNode;
}

export function SectionCard({
  title,
  description,
  children,
  className,
  actions,
}: SectionCardProps) {
  return (
    <div
      className={cn(
        "bg-card rounded-lg border border-border p-5 shadow-sm",
        className
      )}
    >
      {(title || actions) && (
        <div className="flex items-center justify-between mb-4">
          <div>
            {title && (
              <h3 className="text-sm font-semibold text-foreground">{title}</h3>
            )}
            {description && (
              <p className="text-xs text-muted-foreground mt-0.5">
                {description}
              </p>
            )}
          </div>
          {actions}
        </div>
      )}
      {children}
    </div>
  );
}
