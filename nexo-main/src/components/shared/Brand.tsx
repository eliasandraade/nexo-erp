import { cn } from "@/lib/utils";
export function Brand({ className }: { className?: string }) {
  return <span className={cn("orken-wordmark text-foreground", className)}>ORKEN<span aria-hidden className="ml-0.5 text-primary">.</span></span>;
}
