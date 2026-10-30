import { useEffect, useState } from "react";
import { useLocation } from "react-router-dom";
import { ShieldAlert, LogOut } from "lucide-react";
import {
  apiClient,
  endImpersonationSession,
  getImpersonation,
  type ImpersonationInfo,
} from "@/services/api-client";

const formatTime = (iso: string) =>
  new Date(iso).toLocaleTimeString("pt-BR", { hour: "2-digit", minute: "2-digit" });

/**
 * Fixed top banner shown ONLY in a tab that entered via platform impersonation (tab-scoped marker in
 * sessionStorage, written by ImpersonatePage) — never in the platform admin's own tabs.
 *
 * "Sair do acesso" ends the access on the server (audited), drops this tab's token (sessionStorage only — the
 * administrator's own localStorage session is never touched) and closes the tab (or shows "Acesso encerrado").
 */
export function ImpersonationBanner() {
  const location = useLocation();
  const [info, setInfo] = useState<ImpersonationInfo | null>(null);
  const [leaving, setLeaving] = useState(false);

  useEffect(() => {
    const current = getImpersonation();
    const active = current && !current.ended ? current : null;
    setInfo(active);
    // Push the whole app down so the fixed bar never covers the real chrome.
    document.body.style.paddingTop = active ? "2.25rem" : "";
    return () => {
      document.body.style.paddingTop = "";
    };
  }, [location.pathname]);

  // The access ends at expiresAt even without a request to find it out.
  useEffect(() => {
    if (!info?.expiresAt) return;
    const ms = new Date(info.expiresAt).getTime() - Date.now();
    if (Number.isNaN(ms)) return;
    const timer = setTimeout(() => endImpersonationSession("impersonation_ended"), Math.max(ms, 0));
    return () => clearTimeout(timer);
  }, [info?.expiresAt]);

  if (!info) return null;

  const exit = async () => {
    setLeaving(true);
    try {
      await apiClient.post("/auth/impersonation/end");
    } catch {
      // already expired or ended: leaving locally is what matters
    }
    document.body.style.paddingTop = "";
    setInfo(null);
    endImpersonationSession("impersonation_left");
    window.close();   // a tab opened by the platform can close itself; otherwise "Acesso encerrado" stays
  };

  return (
    <div
      role="status"
      className="fixed top-0 inset-x-0 z-[100] h-9 flex items-center justify-center gap-3 bg-amber-500 text-amber-950 text-xs sm:text-[13px] font-medium px-3 shadow-sm"
    >
      <ShieldAlert className="h-3.5 w-3.5 shrink-0" aria-hidden />
      <span className="truncate">
        Acesso da plataforma — você está como{" "}
        <strong>{info.user || "usuário"}</strong> da empresa{" "}
        <strong>{info.tenant || "—"}</strong>
        {info.expiresAt && <> · até {formatTime(info.expiresAt)}</>}
      </span>
      <button
        type="button"
        onClick={exit}
        disabled={leaving}
        className="inline-flex items-center gap-1 shrink-0 rounded-md bg-amber-950/10 hover:bg-amber-950/20 px-2 py-1 transition-colors disabled:opacity-60"
      >
        <LogOut className="h-3 w-3" aria-hidden />
        {leaving ? "Saindo…" : "Sair do acesso"}
      </button>
    </div>
  );
}
