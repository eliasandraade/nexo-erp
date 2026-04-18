import { useEffect } from "react";
import { useNavigate } from "react-router-dom";
import { setTokens, TOKEN_KEYS } from "@/services/api-client";

/**
 * Landing page for platform admin impersonation.
 * The platform detail page stores tokens in localStorage under special keys,
 * then opens this page in a new tab. This page reads them, loads the session,
 * and redirects to /dashboard.
 */
export default function ImpersonatePage() {
  const navigate = useNavigate();

  useEffect(() => {
    const accessToken  = localStorage.getItem("nexo:impersonate:token");
    const refreshToken = localStorage.getItem("nexo:impersonate:refresh");
    const sessionRaw   = localStorage.getItem("nexo:impersonate:session");

    // Clean up keys immediately
    localStorage.removeItem("nexo:impersonate:token");
    localStorage.removeItem("nexo:impersonate:refresh");
    localStorage.removeItem("nexo:impersonate:session");

    if (!accessToken || !refreshToken || !sessionRaw) {
      navigate("/login", { replace: true });
      return;
    }

    try {
      const session = JSON.parse(sessionRaw);
      setTokens(accessToken, refreshToken);
      localStorage.setItem(TOKEN_KEYS.session, JSON.stringify(session));
      navigate("/dashboard", { replace: true });
    } catch {
      navigate("/login", { replace: true });
    }
  }, [navigate]);

  return (
    <div className="min-h-screen flex items-center justify-center bg-background">
      <div className="text-center space-y-3">
        <div className="w-8 h-8 border-2 border-primary border-t-transparent rounded-full animate-spin mx-auto" />
        <p className="text-sm text-muted-foreground">Entrando como cliente...</p>
      </div>
    </div>
  );
}
