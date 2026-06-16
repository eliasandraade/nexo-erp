import { useEffect, useState } from "react";
import { useLocation } from "react-router-dom";
import { ShieldAlert, LogOut } from "lucide-react";
import { useAuth } from "@/modules/auth/context/AuthContext";

const KEY = "orken:impersonation";

interface ImpersonationInfo {
  tenant: string;
  user: string;
}

/**
 * Fixed top banner shown ONLY in a tab that entered via platform impersonation.
 * The marker is written (tab-scoped, sessionStorage) by ImpersonatePage; this reads
 * it on every navigation so the operator always knows they are acting as someone else.
 * "Sair da impersonação" clears the marker and logs out.
 *
 * Tab-scoped (sessionStorage) → never appears in the platform admin's own tabs.
 */
export function ImpersonationBanner() {
  const location = useLocation();
  const { logout } = useAuth();
  const [info, setInfo] = useState<ImpersonationInfo | null>(null);

  useEffect(() => {
    let parsed: ImpersonationInfo | null = null;
    try {
      const raw = sessionStorage.getItem(KEY);
      parsed = raw ? (JSON.parse(raw) as ImpersonationInfo) : null;
    } catch {
      parsed = null;
    }
    setInfo(parsed);
    // Push the whole app down so the fixed bar never covers the real chrome.
    document.body.style.paddingTop = parsed ? "2.25rem" : "";
    return () => {
      document.body.style.paddingTop = "";
    };
  }, [location.pathname]);

  if (!info) return null;

  const exit = () => {
    sessionStorage.removeItem(KEY);
    document.body.style.paddingTop = "";
    setInfo(null);
    logout();
  };

  return (
    <div className="fixed top-0 inset-x-0 z-[100] h-9 flex items-center justify-center gap-3 bg-amber-500 text-amber-950 text-xs sm:text-[13px] font-medium px-3 shadow-sm">
      <ShieldAlert className="h-3.5 w-3.5 shrink-0" />
      <span className="truncate">
        Modo impersonation — você está como{" "}
        <strong>{info.user || "usuário"}</strong> do cliente{" "}
        <strong>{info.tenant || "—"}</strong>.
      </span>
      <button
        onClick={exit}
        className="inline-flex items-center gap-1 shrink-0 rounded-md bg-amber-950/10 hover:bg-amber-950/20 px-2 py-1 transition-colors"
      >
        <LogOut className="h-3 w-3" />
        Sair da impersonação
      </button>
    </div>
  );
}
