import { useState, type FormEvent } from "react";
import { useNavigate, Link } from "react-router-dom";
import { Eye, EyeOff, Loader2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { useAuth } from "../context/AuthContext";

export default function LoginPage() {
  const { login } = useAuth();
  const navigate = useNavigate();

  const [loginField, setLoginField] = useState("");
  const [password, setPassword] = useState("");
  const [showPassword, setShowPassword] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    setError(null);

    if (!loginField.trim()) {
      setError("Informe o login ou e-mail.");
      return;
    }
    if (!password) {
      setError("Informe a senha.");
      return;
    }

    setLoading(true);
    const { error: err, type } = await login({ login: loginField.trim(), password });
    setLoading(false);

    if (err) {
      setError(err);
    } else {
      navigate(type === "platform" ? "/platform" : "/dashboard", { replace: true });
    }
  }

  return (
    <div className="bg-card border border-border rounded-xl shadow-sm p-8 space-y-6">
      <div className="space-y-1">
        <h1 className="text-xl font-semibold text-foreground">Bem-vindo de volta.</h1>
        <p className="text-sm text-muted-foreground">Entre com seus dados para continuar.</p>
      </div>

      {/* Form */}
      <form onSubmit={handleSubmit} className="space-y-4" noValidate>
        <div className="space-y-1.5">
          <Label htmlFor="login">Login ou e-mail</Label>
          <Input
            id="login"
            type="text"
            autoComplete="username"
            placeholder="seu.login ou email@empresa.com"
            value={loginField}
            onChange={(e) => setLoginField(e.target.value)}
            disabled={loading}
            autoFocus
          />
        </div>

        <div className="space-y-1.5">
          <Label htmlFor="password">Senha</Label>
          <div className="relative">
            <Input
              id="password"
              type={showPassword ? "text" : "password"}
              autoComplete="current-password"
              placeholder="••••••••"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              disabled={loading}
              className="pr-10"
            />
            <button
              type="button"
              onClick={() => setShowPassword((v) => !v)}
              className="absolute right-3 top-1/2 -translate-y-1/2 text-muted-foreground hover:text-foreground transition-colors"
              tabIndex={-1}
              aria-label={showPassword ? "Ocultar senha" : "Mostrar senha"}
            >
              {showPassword ? (
                <EyeOff className="h-4 w-4" />
              ) : (
                <Eye className="h-4 w-4" />
              )}
            </button>
          </div>
        </div>

        {error && (
          <p className="text-sm text-destructive bg-destructive/10 border border-destructive/20 rounded-lg px-3 py-2">
            {error}
          </p>
        )}

        <Button type="submit" className="w-full" disabled={loading}>
          {loading && <Loader2 className="h-4 w-4 mr-2 animate-spin" />}
          {loading ? "Entrando..." : "Entrar"}
        </Button>
      </form>

      {/* Create account link */}
      <p className="text-center text-sm text-muted-foreground">
        Não tem conta?{" "}
        <Link to="/register" className="text-primary hover:underline font-medium">
          Criar conta
        </Link>
      </p>
    </div>
  );
}
