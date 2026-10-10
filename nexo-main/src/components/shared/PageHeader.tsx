import { cn } from "@/lib/utils";
interface PageHeaderProps { title: string; description?: string; actions?: React.ReactNode; className?: string; eyebrow?: string; }
export function PageHeader({ title, description, actions, className, eyebrow }: PageHeaderProps) {
  return <header className={cn("page-heading", className)}>
    <div className="min-w-0 flex-1">
      {eyebrow && <p className="mb-2 text-xs font-medium text-muted-foreground">{eyebrow}</p>}
      <h1 className="break-words text-[28px] font-semibold leading-tight tracking-tight">{title}</h1>
      {description && <p className="mt-2 max-w-prose text-sm text-muted-foreground">{description}</p>}
    </div>
    {actions && <div className="page-actions">{actions}</div>}
  </header>;
}
