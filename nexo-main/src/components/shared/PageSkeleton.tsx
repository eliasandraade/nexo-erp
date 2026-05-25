import { cn } from "@/lib/utils";

interface SkeletonProps {
  className?: string;
}

export function Skeleton({ className }: SkeletonProps) {
  return (
    <div className={cn("animate-pulse bg-muted rounded", className)} />
  );
}

export function PageSkeleton() {
  return (
    <div className="flex flex-col gap-6 p-6">
      <div className="flex items-center gap-3">
        <Skeleton className="w-[3px] h-8 rounded-full" />
        <div className="space-y-2">
          <Skeleton className="h-5 w-48" />
          <Skeleton className="h-3 w-72" />
        </div>
      </div>

      <div className="grid grid-cols-2 md:grid-cols-4 gap-3">
        {Array.from({ length: 4 }).map((_, i) => (
          <div key={i} className="bg-card rounded-lg border border-border p-4 space-y-2">
            <Skeleton className="h-3 w-24" />
            <Skeleton className="h-6 w-16" />
          </div>
        ))}
      </div>

      <div className="bg-card rounded-lg border border-border p-5 space-y-3">
        <div className="flex items-center gap-2">
          <Skeleton className="h-7 w-64" />
          <Skeleton className="h-7 w-24 ml-auto" />
        </div>
        {Array.from({ length: 6 }).map((_, i) => (
          <Skeleton key={i} className="h-9 w-full" />
        ))}
      </div>
    </div>
  );
}

export function CardSkeleton({ className }: SkeletonProps) {
  return (
    <div className={cn("bg-card rounded-lg border border-border p-4 space-y-2", className)}>
      <Skeleton className="h-3 w-24" />
      <Skeleton className="h-6 w-16" />
      <Skeleton className="h-3 w-32" />
    </div>
  );
}

export function TableSkeleton({ rows = 5 }: { rows?: number }) {
  return (
    <div className="space-y-0">
      <div className="flex gap-3 px-3 py-2 border-b border-border bg-muted/40">
        {[40, 120, 80, 80, 60].map((w, i) => (
          <Skeleton key={i} className="h-3" style={{ width: w }} />
        ))}
      </div>
      {Array.from({ length: rows }).map((_, i) => (
        <div key={i} className="flex gap-3 px-3 py-2.5 border-b border-border/60">
          {[40, 120, 80, 80, 60].map((w, j) => (
            <Skeleton key={j} className="h-3.5" style={{ width: w }} />
          ))}
        </div>
      ))}
    </div>
  );
}
