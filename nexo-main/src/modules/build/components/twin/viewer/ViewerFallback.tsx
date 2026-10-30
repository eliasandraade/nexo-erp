import { Component, type ReactNode } from "react";
import { MonitorX } from "lucide-react";

const TEXT: Record<"webgl" | "load" | "crash", string> = {
  webgl: "Este navegador não conseguiu iniciar a visualização 3D (WebGL indisponível ou desativado). "
    + "Os estados, vínculos e a lista de elementos continuam disponíveis abaixo.",
  load: "Não foi possível carregar o arquivo de visualização. O link expira em 15 minutos: recarregue a aba para gerar outro.",
  crash: "A visualização 3D falhou neste dispositivo. Os estados, vínculos e a lista de elementos continuam disponíveis.",
};

/** Friendly replacement for the 3D canvas — the rest of the tab keeps working. */
export function ViewerFallback({ reason, detail }: { reason: "webgl" | "load" | "crash"; detail?: string | null }) {
  return (
    <div role="status" data-testid="viewer-fallback"
      className="flex h-full min-h-[12rem] w-full flex-col items-center justify-center gap-2 rounded-md border border-dashed border-border bg-muted/30 p-4 text-center">
      <MonitorX className="h-6 w-6 text-muted-foreground" aria-hidden />
      <p className="max-w-md text-sm text-muted-foreground">{TEXT[reason]}</p>
      {detail && <p className="max-w-md break-words text-[11px] text-muted-foreground/80">{detail}</p>}
    </div>
  );
}

/** Catches a crash inside the lazy viewer (chunk load failure, WebGL loss) and shows the fallback instead. */
export class ViewerBoundary extends Component<{ children: ReactNode }, { failed: boolean }> {
  state = { failed: false };
  static getDerivedStateFromError() { return { failed: true }; }
  componentDidCatch(error: unknown) { console.warn("3D viewer failed:", error); }
  render() { return this.state.failed ? <ViewerFallback reason="crash" /> : this.props.children; }
}
