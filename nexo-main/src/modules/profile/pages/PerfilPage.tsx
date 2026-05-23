import { type FormEvent, useState } from "react";
import { useMutation, useQuery } from "@tanstack/react-query";
import { Eye, EyeOff, KeyRound, Loader2, ShieldCheck } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Skeleton } from "@/components/ui/skeleton";
import { PageHeader } from "@/components/shared/PageHeader";
import { SectionCard } from "@/components/shared/SectionCard";
import { StatusBadge } from "@/components/shared/StatusBadge";
import { useAuth } from "@/modules/auth/context/AuthContext";
import {
  roleLabels,
  statusLabels,
  statusVariant,
} from "@/modules/users/types";
import { profileService } from "../services/profileService";
import { formatDate, formatDateTime } from "@/lib/formatters";

// ─── Sub-components ───────────────────────────────────────────────────────────

function ProfileField({
  label,
  value,
}: {
  label: string;
  value: React.ReactNode;
}) {
  return (
    <div className="flex flex-col gap-0.5 py-2.5 border-b border-border last:border-0">
      <dt className="text-xs text-muted-foreground">{label}</dt>
      <dd className="text-sm font-medium text-foreground">{value ?? "—"}</dd>
    </div>
  );
}

function getInitials(name: string): string {
  return name
    .split(" ")
    .filter(Boolean)
    .slice(0, 2)
    .map((w) => w[0].toUpperCase())
    .join("");
}

// ─── Loading skeleton ─────────────────────────────────────────────────────────

function PerfilSkeleton() {
  return (
    <div className="space-y-6">
      <div className="flex items-start justify-between">
        <div className="space-y-1.5">
          <Skeleton className="h-6 w-36" />
          <Skeleton className="h-4 w-64" />
        </div>
      </div>
      <div className="bg-card rounded-xl border border-border p-5">
        <div className="flex items-center gap-4">
          <Skeleton className="w-16 h-16 rounded-full" />
          <div className="space-y-2">
            <Skeleton className="h-5 w-48" />
            <Skeleton className="h-4 w-32" />
          </div>
        </div>
      </div>
      <div className="grid lg:grid-cols-2 gap-6">
        <div className="bg-card rounded-xl border border-border p-5 space-y-1">
          <Skeleton className="h-4 w-32 mb-4" />
          {Array.from({ length: 7 }).map((_, i) => (
            <div key={i} className="py-2.5 border-b border-border last:border-0 space-y-1">
              <Skeleton className="h-3 w-20" />
              <Skeleton className="h-4 w-40" />
            </div>
          ))}
        </div>
        <div className="bg-card rounded-xl border border-border p-5 space-y-1">
          <Skeleton className="h-4 w-44 mb-4" />
          {Array.from({ length: 3 }).map((_, i) => (
            <div key={i} className="py-2.5 border-b border-border last:border-0 space-y-1">
              <Skeleton className="h-3 w-24" />
              <Skeleton className="h-4 w-36" />
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}

// ─── Page ─────────────────────────────────────────────────────────────────────

export default function PerfilPage() {
  const { session } = useAuth();

  const { data: user, isLoading } = useQuery({
    queryKey: ["profile", session?.userId],
    queryFn: () => profileService.getProfile(session!.userId),
    enabled: !!session,
  });

  // Password form state
  const [newPassword, setNewPassword] = useState("");
  const [confirmPassword, setConfirmPassword] = useState("");
  const [showNew, setShowNew] = useState(false);
  const [showConfirm, setShowConfirm] = useState(false);

  const changePasswordMutation = useMutation({
    mutationFn: () =>
      profileService.changePassword(session!.userId, {
        newPassword,
        confirmPassword,
      }),
    onSuccess: () => {
      toast.success("Senha alterada com sucesso.");
      setNewPassword("");
      setConfirmPassword("");
    },
    onError: (err: Error) => {
      toast.error(err.message);
    },
  });

  function handlePasswordSubmit(e: FormEvent) {
    e.preventDefault();
    if (!newPassword.trim()) {
      toast.error("Informe a nova senha.");
      return;
    }
    if (newPassword.length < 6) {
      toast.error("A nova senha deve ter pelo menos 6 caracteres.");
      return;
    }
    if (newPassword !== confirmPassword) {
      toast.error("As senhas não coincidem.");
      return;
    }
    changePasswordMutation.mutate();
  }

  if (isLoading || !user) {
    return <PerfilSkeleton />;
  }

  const initials = getInitials(user.name);

  return (
    <div className="space-y-6">
      <PageHeader
        title="Meu perfil"
        description="Consulte e atualize as informações da sua conta."
      />

      {/* ── Profile summary ─────────────────────────────────────────────── */}
      <SectionCard>
        <div className="flex items-center gap-5">
          <div className="w-16 h-16 rounded-full bg-primary flex items-center justify-center shrink-0">
            <span className="text-xl font-bold text-primary-foreground">
              {initials}
            </span>
          </div>
          <div className="min-w-0">
            <h3 className="text-base font-semibold text-foreground leading-tight">
              {user.name}
            </h3>
            <div className="flex flex-wrap items-center gap-2 mt-1.5">
              <span className="text-sm text-muted-foreground">
                {roleLabels[user.role]}
              </span>
              {user.store && user.store !== "—" && (
                <>
                  <span className="text-muted-foreground text-xs">·</span>
                  <span className="text-sm text-muted-foreground">
                    {user.store}
                  </span>
                </>
              )}
              <StatusBadge
                label={statusLabels[user.status]}
                variant={statusVariant[user.status]}
              />
            </div>
          </div>
        </div>
      </SectionCard>

      {/* ── Dados + Conta ────────────────────────────────────────────────── */}
      <div className="grid lg:grid-cols-2 gap-6">
        {/* Dados principais */}
        <SectionCard title="Dados principais">
          <dl>
            <ProfileField label="Nome completo" value={user.name} />
            <ProfileField label="E-mail" value={user.email} />
            <ProfileField label="Login" value={user.login} />
            <ProfileField
              label="Telefone"
              value={user.phone || "—"}
            />
            <ProfileField
              label="Perfil"
              value={roleLabels[user.role]}
            />
            <ProfileField
              label="Loja vinculada"
              value={
                user.store && user.store !== "—" ? user.store : (
                  <span className="text-muted-foreground">Todas as lojas</span>
                )
              }
            />
            <ProfileField
              label="Status"
              value={
                <StatusBadge
                  label={statusLabels[user.status]}
                  variant={statusVariant[user.status]}
                />
              }
            />
          </dl>
        </SectionCard>

        {/* Informações da conta */}
        <SectionCard title="Informações da conta">
          <dl>
            <ProfileField
              label="Último acesso"
              value={
                user.lastAccess
                  ? formatDateTime(user.lastAccess)
                  : "Nenhum registro"
              }
            />
            <ProfileField
              label="Criado em"
              value={user.createdAt ? formatDate(user.createdAt) : "—"}
            />
            <ProfileField
              label="Criado por"
              value={user.createdBy || "—"}
            />
            <ProfileField
              label="Última alteração de senha"
              value={
                user.lastPasswordChange
                  ? formatDate(user.lastPasswordChange)
                  : "Nunca alterada"
              }
            />
            <ProfileField
              label="Última atualização"
              value={user.updatedAt ? formatDateTime(user.updatedAt) : "—"}
            />
          </dl>
        </SectionCard>
      </div>

      {/* ── Segurança ───────────────────────────────────────────────────── */}
      <SectionCard
        title="Segurança"
        description="Altere sua senha de acesso ao sistema."
        actions={
          <div className="flex items-center gap-1.5 text-xs text-muted-foreground">
            <ShieldCheck className="h-3.5 w-3.5" />
            <span>Validação simples</span>
          </div>
        }
      >
        <form onSubmit={handlePasswordSubmit} noValidate>
          <div className="grid sm:grid-cols-2 gap-4 max-w-lg">
            {/* Nova senha */}
            <div className="space-y-1.5">
              <Label htmlFor="newPassword">Nova senha</Label>
              <div className="relative">
                <Input
                  id="newPassword"
                  type={showNew ? "text" : "password"}
                  placeholder="Mínimo 6 caracteres"
                  value={newPassword}
                  onChange={(e) => setNewPassword(e.target.value)}
                  disabled={changePasswordMutation.isPending}
                  className="pr-10"
                  autoComplete="new-password"
                />
                <button
                  type="button"
                  onClick={() => setShowNew((v) => !v)}
                  className="absolute right-3 top-1/2 -translate-y-1/2 text-muted-foreground hover:text-foreground transition-colors"
                  tabIndex={-1}
                  aria-label={showNew ? "Ocultar" : "Mostrar"}
                >
                  {showNew ? (
                    <EyeOff className="h-4 w-4" />
                  ) : (
                    <Eye className="h-4 w-4" />
                  )}
                </button>
              </div>
            </div>

            {/* Confirmar nova senha */}
            <div className="space-y-1.5">
              <Label htmlFor="confirmPassword">Confirmar nova senha</Label>
              <div className="relative">
                <Input
                  id="confirmPassword"
                  type={showConfirm ? "text" : "password"}
                  placeholder="Repita a nova senha"
                  value={confirmPassword}
                  onChange={(e) => setConfirmPassword(e.target.value)}
                  disabled={changePasswordMutation.isPending}
                  className="pr-10"
                  autoComplete="new-password"
                />
                <button
                  type="button"
                  onClick={() => setShowConfirm((v) => !v)}
                  className="absolute right-3 top-1/2 -translate-y-1/2 text-muted-foreground hover:text-foreground transition-colors"
                  tabIndex={-1}
                  aria-label={showConfirm ? "Ocultar" : "Mostrar"}
                >
                  {showConfirm ? (
                    <EyeOff className="h-4 w-4" />
                  ) : (
                    <Eye className="h-4 w-4" />
                  )}
                </button>
              </div>
            </div>
          </div>

          {/* Match hint */}
          {newPassword && confirmPassword && newPassword !== confirmPassword && (
            <p className="mt-2 text-xs text-destructive flex items-center gap-1">
              As senhas não coincidem.
            </p>
          )}
          {newPassword && confirmPassword && newPassword === confirmPassword && (
            <p className="mt-2 text-xs text-success flex items-center gap-1">
              As senhas coincidem.
            </p>
          )}

          <div className="mt-4">
            <Button
              type="submit"
              disabled={changePasswordMutation.isPending}
              size="sm"
            >
              {changePasswordMutation.isPending ? (
                <>
                  <Loader2 className="h-4 w-4 mr-2 animate-spin" />
                  Alterando...
                </>
              ) : (
                <>
                  <KeyRound className="h-4 w-4 mr-2" />
                  Alterar senha
                </>
              )}
            </Button>
          </div>
        </form>
      </SectionCard>
    </div>
  );
}
