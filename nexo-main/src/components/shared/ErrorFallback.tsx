import { useNavigate } from "react-router-dom";
import { RefreshCw, Home } from "lucide-react";
import { Brand } from "./Brand";
import { Button } from "@/components/ui/button";

/** Route failure with safe recovery; never exposes an internal stack trace. */
export function ErrorFallback({ onRetry }: { onRetry: () => void }) {
  const navigate = useNavigate();
  function goHome() { onRetry(); navigate("/dashboard", { replace: true }); }
  return <main className="flex min-h-dvh items-center justify-center bg-background px-6 text-foreground">
    <div className="w-full max-w-md">
      <Brand />
      <div role="alert" className="mt-10 border-t border-border pt-6">
        <h1 className="text-2xl font-semibold tracking-tight">Não foi possível abrir esta área</h1>
        <p className="mt-3 text-sm leading-relaxed text-muted-foreground">Tente carregar novamente. Se o problema continuar, volte ao início e contate o administrador.</p>
      </div>
      <div className="mt-6 flex flex-wrap gap-3">
        <Button onClick={onRetry}><RefreshCw className="mr-2 h-4 w-4" />Tentar novamente</Button>
        <Button variant="outline" onClick={goHome}><Home className="mr-2 h-4 w-4" />Voltar ao início</Button>
      </div>
    </div>
  </main>;
}
