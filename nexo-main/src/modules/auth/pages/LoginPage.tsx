import { useState, type FormEvent } from "react";
import { useNavigate, Link } from "react-router-dom";
import { Eye, EyeOff, Loader2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { useAuth } from "../context/AuthContext";

const demoUsers = [
  { login: "carlos.andrade", password: "diretor123", label: "Diretoria" },
  { login: "fernanda.lima",  password: "gerente123", label: "Gerente" },
  { login: "rafael.souza",   password: "vendedor123", label: "Vendedor" },
  { login: "juliana.costa",  password: "estoque123",  label: "Estoquista" },
];

export default function LoginPage() {
  const { login } = useAuth();
  const navigate = useNavigate();

  const [loginField, setLoginField] = useState("");
  const [password, setPassword] = useState("");
  const [showPassword, setShowPassword] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const [showDemo, setShowDemo] = useState(false);

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

  function fillDemo(u: (typeof demoUsers)[number]) {
    setLoginField(u.login);
    setPassword(u.password);
    setError(null);
  }

  return (
    <div className="bg-card border border-border rounded-xl shadow-sm p-8 space-y-6">
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

      {/* Demo users */}
      <div className="border-t border-border pt-4">
        <button
          type="button"
          onClick={() => setShowDemo((v) => !v)}
          className="text-xs text-muted-foreground hover:text-foreground transition-colors w-full text-center"
        >
          {showDemo ? "Ocultar" : "Ver"} usuários de demonstração
        </button>

        {showDemo && (
          <div className="mt-3 grid grid-cols-2 gap-2">
            {demoUsers.map((u) => (
              <button
                key={u.login}
                type="button"
                onClick={() => fillDemo(u)}
                className="text-left px-3 py-2 rounded-lg border border-border hover:border-secondary/50 hover:bg-muted transition-colors"
              >
                <p className="text-xs font-medium text-foreground">{u.label}</p>
                <p className="text-[11px] text-muted-foreground truncate">
                  {u.login}
                </p>
              </button>
            ))}
          </div>
        )}
      </div>
    </div>
  );
}
