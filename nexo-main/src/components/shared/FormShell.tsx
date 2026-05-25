import { cn } from "@/lib/utils";

interface FormShellProps {
  children: React.ReactNode;
  className?: string;
  maxWidth?: "sm" | "md" | "lg" | "xl" | "full";
}

const maxWidthMap: Record<NonNullable<FormShellProps["maxWidth"]>, string> = {
  sm: "max-w-sm",
  md: "max-w-md",
  lg: "max-w-lg",
  xl: "max-w-xl",
  full: "max-w-full",
};

export function FormShell({ children, className, maxWidth = "lg" }: FormShellProps) {
  return (
    <div className={cn("w-full", maxWidthMap[maxWidth], className)}>
      {children}
    </div>
  );
}

interface FormRowProps {
  children: React.ReactNode;
  className?: string;
  cols?: 1 | 2 | 3;
}

export function FormRow({ children, className, cols = 1 }: FormRowProps) {
  return (
    <div className={cn(
      "grid gap-4",
      cols === 1 && "grid-cols-1",
      cols === 2 && "grid-cols-1 sm:grid-cols-2",
      cols === 3 && "grid-cols-1 sm:grid-cols-3",
      className
    )}>
      {children}
    </div>
  );
}

interface FormSectionProps {
  title?: string;
  children: React.ReactNode;
  className?: string;
}

export function FormSection({ title, children, className }: FormSectionProps) {
  return (
    <div className={cn("space-y-4", className)}>
      {title && (
        <p className="text-[11px] font-semibold uppercase tracking-[0.1em] text-muted-foreground border-b border-border pb-2">
          {title}
        </p>
      )}
      {children}
    </div>
  );
}
