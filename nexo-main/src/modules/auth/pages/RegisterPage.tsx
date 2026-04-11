import { useState, type FormEvent } from "react";
import { useNavigate, Link } from "react-router-dom";
import { Loader2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import * as authService from "../services/authService";

export default function RegisterPage() {
  const navigate = useNavigate();
  const [name, setName] = useState("");
  const [email, setEmail] = useState("");
  const [password, setPassword] = useState("");
  const [confirm, setConfirm] = useState("");
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  async function handleSubmit(e: FormEvent) {
    e.preventDefault();
    setError(null);
    if (!name.trim()) return setError("Informe seu nome.");
    if (!email.trim()) return setError("Informe seu e-mail.");
    if (password.length < 6) return setError("A senha deve ter no mínimo 6 caracteres.");
    if (password !== confirm) return setError("As senhas não conferem.");

    setLoading(true);
    const result = await authService.register({ name: name.trim(), email: email.trim(), password });
    setLoading(false);

    if (result.error) {
      setError(result.error);
    } else {
      navigate(`/check-email?email=${encodeURIComponent(email.trim())}`);
    }
  }

  return (
    <div className="bg-card border border-border rounded-xl shadow-sm p-8 space-y-6">
      <div className="space-y-1">
        <h1 className="text-xl font-semibold text-foreground">Criar conta</h1>
        <p className="text-sm text-muted-foreground">Comece grátis em menos de 1 minuto.</p>
      </div>

      <form onSubmit={handleSubmit} className="space-y-4" noValidate>
        <div className="space-y-1.5">
          <Label htmlFor="name">Seu nome</Label>
          <Input
            id="name"
            type="text"
            placeholder="João Silva"
            value={name}
            onChange={(e) => setName(e.target.value)}
            disabled={loading}
            autoFocus
          />
        </div>
        <div className="space-y-1.5">
          <Label htmlFor="email">E-mail</Label>
          <Input
            id="email"
            type="email"
            placeholder="joao@empresa.com"
            value={email}
            onChange={(e) => setEmail(e.target.value)}
            disabled={loading}
          />
        </div>
        <div className="space-y-1.5">
          <Label htmlFor="password">Senha</Label>
          <Input
            id="password"
            type="password"
            placeholder="Mínimo 6 caracteres"
            value={password}
            onChange={(e) => setPassword(e.target.value)}
            disabled={loading}
          />
        </div>
        <div className="space-y-1.5">
          <Label htmlFor="confirm">Confirmar senha</Label>
          <Input
            id="confirm"
            type="password"
            placeholder="Repita a senha"
            value={confirm}
            onChange={(e) => setConfirm(e.target.value)}
            disabled={loading}
          />
        </div>

        {error && (
          <p className="text-sm text-destructive bg-destructive/10 border border-destructive/20 rounded-lg px-3 py-2">
            {error}
          </p>
        )}

        <Button type="submit" className="w-full" disabled={loading}>
          {loading && <Loader2 className="h-4 w-4 mr-2 animate-spin" />}
          {loading ? "Criando conta..." : "Criar conta"}
        </Button>
      </form>

      <p className="text-center text-sm text-muted-foreground">
        Já tem conta?{" "}
        <Link to="/login" className="text-primary hover:underline font-medium">
          Entrar
        </Link>
      </p>
    </div>
  );
}
