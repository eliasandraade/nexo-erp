import { cn } from "@/lib/utils";

interface ActionBarProps {
  children: React.ReactNode;
  className?: string;
  align?: "left" | "right" | "between";
}

export function ActionBar({ children, className, align = "right" }: ActionBarProps) {
  return (
    <div className={cn(
      "flex items-center gap-2",
      align === "right" && "justify-end",
      align === "left" && "justify-start",
      align === "between" && "justify-between",
      className
    )}>
      {children}
    </div>
  );
}
