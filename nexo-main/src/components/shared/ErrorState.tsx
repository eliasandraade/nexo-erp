import { AlertTriangle, RefreshCw, WifiOff } from "lucide-react";
import { cn } from "@/lib/utils";
import { Button } from "@/components/ui/button";

interface ErrorStateProps {
  title?: string;
  description?: string;
  onRetry?: () => void;
  type?: "generic" | "network" | "notfound";
  className?: string;
  compact?: boolean;
}

const typeConfig = {
  generic: {
    icon: AlertTriangle,
    title: "Algo deu errado",
    description: "Ocorreu um erro ao carregar os dados.",
  },
  network: {
    icon: WifiOff,
    title: "Sem conexão",
    description: "Verifique sua conexão e tente novamente.",
  },
  notfound: {
    icon: AlertTriangle,
    title: "Não encontrado",
    description: "O recurso solicitado não existe ou foi removido.",
  },
} as const;

export function ErrorState({
  title,
  description,
  onRetry,
  type = "generic",
  className,
  compact = false,
}: ErrorStateProps) {
  const config = typeConfig[type];
  const Icon = config.icon;
  const displayTitle = title ?? config.title;
  const displayDesc = description ?? config.description;

  return (
    <div className={cn(
      "flex flex-col items-center justify-center text-center",
      compact ? "py-8" : "py-14",
      className
    )}>
      <div className="w-10 h-10 rounded-lg border border-destructive/20 bg-destructive/5 flex items-center justify-center mb-3">
        <Icon className="h-4 w-4 text-destructive" />
      </div>

      <p className="text-[13.5px] font-semibold text-foreground">{displayTitle}</p>
      <p className="text-[12.5px] text-muted-foreground mt-1 max-w-xs leading-relaxed">
        {displayDesc}
      </p>

      {onRetry && (
        <Button
          variant="outline"
          size="sm"
          onClick={onRetry}
          className="mt-4 h-7 text-[12px] gap-1.5"
        >
          <RefreshCw className="h-3 w-3" />
          Tentar novamente
        </Button>
      )}
    </div>
  );
}
