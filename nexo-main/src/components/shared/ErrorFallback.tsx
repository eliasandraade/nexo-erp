import { useNavigate } from "react-router-dom";
import { RefreshCw, Home, TriangleAlert } from "lucide-react";

/**
 * Premium, Orken-branded fallback shown by AppErrorBoundary when a route
 * subtree throws. No stack trace is ever exposed to the end user.
 */
export function ErrorFallback({ onRetry }: { onRetry: () => void }) {
  const navigate = useNavigate();

  function goHome() {
    onRetry(); // clear the boundary so the destination renders fresh
    navigate("/dashboard", { replace: true }); // route guards re-route per role/auth
  }

  return (
    <div className="relative flex min-h-screen items-center justify-center overflow-hidden bg-[#0B1020] px-6 text-white">
      {/* Ambient indigo wash — gradient only, no blur filter */}
      <div
        className="pointer-events-none absolute -top-32 left-1/2 h-[340px] w-[640px] -translate-x-1/2"
        style={{
          background:
            "radial-gradient(ellipse at center, rgba(91,77,255,0.16) 0%, transparent 65%)",
        }}
        aria-hidden
      />

      <div className="relative w-full max-w-md text-center">
        <img
          src="/orken_darkmode.png"
          alt="Orken"
          className="mx-auto mb-10 h-5 w-auto select-none"
          draggable={false}
        />

        <div className="mx-auto mb-6 flex h-12 w-12 items-center justify-center rounded-xl border border-white/[0.08] bg-white/[0.03]">
          <TriangleAlert className="h-5 w-5 text-amber-400/90" strokeWidth={1.75} />
        </div>

        <h1 className="font-display text-[24px] font-bold tracking-tight">
          Algo saiu do fluxo
        </h1>
        <p className="mx-auto mt-2.5 max-w-sm text-[14px] leading-relaxed text-slate-400">
          Não foi possível carregar esta área do Orken agora. Tente novamente ou volte
          para o início.
        </p>

        <div className="mt-8 flex flex-col items-center justify-center gap-3 sm:flex-row">
          <button
            type="button"
            onClick={onRetry}
            className="inline-flex h-11 w-full items-center justify-center gap-2 rounded-[10px] bg-[#5B4DFF] px-5 text-[13.5px] font-semibold text-white transition-colors hover:bg-[#4338CA] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#5B4DFF]/50 focus-visible:ring-offset-2 focus-visible:ring-offset-[#0B1020] sm:w-auto"
          >
            <RefreshCw className="h-4 w-4" />
            Tentar novamente
          </button>
          <button
            type="button"
            onClick={goHome}
            className="inline-flex h-11 w-full items-center justify-center gap-2 rounded-[10px] border border-white/[0.1] px-5 text-[13.5px] font-medium text-slate-300 transition-colors hover:border-white/20 hover:text-white sm:w-auto"
          >
            <Home className="h-4 w-4" />
            Voltar ao início
          </button>
        </div>
      </div>
    </div>
  );
}
