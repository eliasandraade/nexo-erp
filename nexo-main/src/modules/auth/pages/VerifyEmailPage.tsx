import { useEffect, useState } from "react";
import { useSearchParams, useNavigate, Link } from "react-router-dom";
import { Loader2, CheckCircle, XCircle } from "lucide-react";
import { Button } from "@/components/ui/button";
import { verifyEmail } from "../services/authService";
import { useAuth } from "../context/AuthContext";

export default function VerifyEmailPage() {
  const [params] = useSearchParams();
  const token = params.get("token") ?? "";
  const navigate = useNavigate();
  const { setSessionFromVerify } = useAuth();
  const [status, setStatus] = useState<"verifying" | "success" | "error">("verifying");

  useEffect(() => {
    if (!token) {
      setStatus("error");
      return;
    }

    verifyEmail(token).then((result) => {
      if (result.success && result.session) {
        setSessionFromVerify(result.session);
        setStatus("success");
        setTimeout(() => navigate("/dashboard", { replace: true }), 1500);
      } else {
        setStatus("error");
      }
    });
  // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  return (
    <div className="bg-card border border-border rounded-xl shadow-sm p-8 space-y-6 text-center">
      {status === "verifying" && (
        <>
          <Loader2 className="h-10 w-10 text-primary animate-spin mx-auto" />
          <p className="text-sm text-muted-foreground">Verificando sua conta...</p>
        </>
      )}
      {status === "success" && (
        <>
          <CheckCircle className="h-12 w-12 text-green-500 mx-auto" />
          <div className="space-y-1">
            <h1 className="text-xl font-semibold text-foreground">Conta verificada!</h1>
            <p className="text-sm text-muted-foreground">Redirecionando para o sistema...</p>
          </div>
        </>
      )}
      {status === "error" && (
        <>
          <XCircle className="h-12 w-12 text-destructive mx-auto" />
          <div className="space-y-1">
            <h1 className="text-xl font-semibold text-foreground">Link inválido</h1>
            <p className="text-sm text-muted-foreground">O link expirou ou já foi usado.</p>
          </div>
          <Button asChild variant="outline" className="w-full">
            <Link to="/login">Ir para o login</Link>
          </Button>
        </>
      )}
    </div>
  );
}
