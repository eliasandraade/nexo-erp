import { useState, Fragment } from "react";
import { useParams, useNavigate } from "react-router-dom";
import {
  ArrowLeft, Store, Users, ExternalLink,
  Check, X, Edit2, Save, Pin, Trash2,
  KeyRound, LogOut as LogOutIcon, StickyNote, PinOff,
  ChevronDown, ChevronUp, Monitor, Wifi, History,
} from "lucide-react";
import { toast } from "sonner";
import { cn } from "@/lib/utils";
import { ConfirmDialog } from "@/components/ConfirmDialog";

import {
  usePlatformTenant,
  useSetTenantStatus,
  useUpdateTenant,
  useGrantModule,
  useRevokeModule,
  useImpersonate,
  useTenantNotes,
  useCreateNote,
  useDeleteNote,
  useToggleNotePin,
  useResetUserPassword,
  useUserSessions,
  useRevokeAllSessions,
  usePlanHistory,
} from "../hooks/usePlatformTenants";

// ─── Constants ────────────────────────────────────────────────────────────────

const TABS = ["Geral", "Módulos", "Histórico", "Usuários", "Notas", "Lojas"] as const;
type Tab = typeof TABS[number];

const MODULE_LABELS: Record<string, string> = {
  varejo:      "Varejo",
  restaurante: "Restaurante",
  build:       "ORKEN Build",
};

const AVAILABLE_MODULES = [
  { key: "varejo",      label: "Varejo" },
  { key: "restaurante", label: "Restaurante" },
  { key: "build",       label: "ORKEN Build" },
];

const ROLE_COLORS: Record<string, string> = {
  Diretoria:  "bg-primary/10 text-primary",
  Gerente:    "bg-blue-500/10 text-blue-600",
  Vendedor:   "bg-green-500/10 text-green-600",
  Estoquista: "bg-amber-500/10 text-amber-600",
};

// ─── Component ───────────────────────────────────────────────────────────────

export default function PlatformTenantDetailPage() {
  const { tenantId } = useParams<{ tenantId: string }>();
  const navigate = useNavigate();
  const { data: tenant, isLoading } = usePlatformTenant(tenantId ?? "");

  const setStatusMut      = useSetTenantStatus(tenantId ?? "");
  const updateMut         = useUpdateTenant(tenantId ?? "");
  const grantMut          = useGrantModule(tenantId ?? "");
  const revokeMut         = useRevokeModule(tenantId ?? "");
  const impersonateMut    = useImpersonate();
  const resetPwMut        = useResetUserPassword(tenantId ?? "");
  const revokeSessionsMut = useRevokeAllSessions(tenantId ?? "");
  const createNoteMut     = useCreateNote(tenantId ?? "");
  const deleteNoteMut     = useDeleteNote(tenantId ?? "");
  const togglePinMut      = useToggleNotePin(tenantId ?? "");
  const { data: notes = [] } = useTenantNotes(tenantId ?? "");
  const { data: planHistory = [], isLoading: historyLoading } = usePlanHistory(tenantId ?? "");

  const [tab, setTab] = useState<Tab>("Geral");
  const [editing, setEditing] = useState(false);
  const [editForm, setEditForm] = useState({
    companyName: "", tradeName: "", taxId: "", email: "", phone: "", businessType: "",
  });

  // User action state
  const [resetTarget, setResetTarget]   = useState<{ id: string; name: string } | null>(null);
  const [newPassword, setNewPassword]   = useState("");
  const [expandedUser, setExpandedUser] = useState<string | null>(null);

  // Confirm dialog state
  const [confirm, setConfirm] = useState<{
    open: boolean;
    title: string;
    description: string;
    variant: "danger" | "warning" | "default";
    onConfirm: () => void;
  }>({ open: false, title: "", description: "", variant: "danger", onConfirm: () => {} });

  const openConfirm = (opts: Omit<typeof confirm, "open">) =>
    setConfirm({ ...opts, open: true });
  const closeConfirm = () =>
    setConfirm(c => ({ ...c, open: false }));

  // Note compose state
  const [noteText, setNoteText]     = useState("");
  const [notePinned, setNotePinned] = useState(false);

  if (isLoading) return <div className="p-6 text-sm text-muted-foreground">Carregando...</div>;
  if (!tenant) return <div className="p-6 text-sm text-muted-foreground">Tenant não encontrado.</div>;

  const startEdit = () => {
    setEditForm({
      companyName:  tenant.companyName,
      tradeName:    tenant.tradeName ?? "",
      taxId:        tenant.taxId,
      email:        tenant.email,
      phone:        tenant.phone ?? "",
      businessType: tenant.businessType ?? "",
    });
    setEditing(true);
  };

  const saveEdit = async () => {
    await updateMut.mutateAsync(editForm);
    setEditing(false);
  };

  const handleImpersonate = async () => {
    try {
      const result = await impersonateMut.mutateAsync(tenantId!);
      // Store impersonation data in localStorage for the new tab
      localStorage.setItem("nexo:impersonate:token",   result.accessToken);
      localStorage.setItem("nexo:impersonate:refresh",  result.refreshToken);
      localStorage.setItem("nexo:impersonate:session",  JSON.stringify(result.session));
      window.open("/impersonate", "_blank");
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : "Erro ao impersonar";
      toast.error(msg);
    }
  };

  const activeModuleKeys = tenant.subscriptions
    .filter(s => s.status === "Active")
    .map(s => s.moduleKey);

  // The backend impersonates the tenant's first ACTIVE Diretoria user — surface it in the confirm.
  const impersonationTarget = tenant.users.find(
    u => u.role === "Diretoria" && u.status === "Active"
  );

  return (
    <div className="p-6 space-y-5 max-w-4xl">
      <ConfirmDialog
        open={confirm.open}
        title={confirm.title}
        description={confirm.description}
        variant={confirm.variant}
        onConfirm={() => { closeConfirm(); confirm.onConfirm(); }}
        onCancel={closeConfirm}
      />

      {/* Header */}
      <div className="flex items-start gap-3">
        <button
          onClick={() => navigate("/platform/tenants")}
          className="p-1.5 rounded-md hover:bg-muted transition-colors mt-0.5"
        >
          <ArrowLeft className="h-4 w-4 text-muted-foreground" />
        </button>
        <div className="flex-1 min-w-0">
          <div className="flex items-center gap-3">
            <h1 className="text-xl font-semibold text-foreground truncate">
              {tenant.tradeName ?? tenant.companyName}
            </h1>
            <StatusBadge status={tenant.status} />
          </div>
          <p className="text-sm text-muted-foreground mt-0.5">{tenant.email} · {tenant.taxId}</p>
        </div>

        {/* Actions */}
        <div className="flex items-center gap-2 shrink-0">
          {/* Status toggle */}
          {tenant.status === "Active" ? (
            <button
              onClick={() => openConfirm({
                title: "Suspender cliente",
                description: `Suspender "${tenant.tradeName ?? tenant.companyName}" bloqueia o acesso de todos os usuários deste cliente até a reativação. Confirmar?`,
                variant: "warning",
                onConfirm: () => setStatusMut.mutate("Suspended"),
              })}
              disabled={setStatusMut.isPending}
              className="h-8 px-3 rounded-lg border border-amber-500/50 text-amber-600 text-xs font-medium hover:bg-amber-500/10 transition-colors disabled:opacity-50"
            >
              Suspender
            </button>
          ) : (
            <button
              onClick={() => openConfirm({
                title: "Reativar cliente",
                description: `Reativar "${tenant.tradeName ?? tenant.companyName}" restaura o acesso dos usuários deste cliente. Confirmar?`,
                variant: "default",
                onConfirm: () => setStatusMut.mutate("Active"),
              })}
              disabled={setStatusMut.isPending}
              className="h-8 px-3 rounded-lg border border-green-500/50 text-green-600 text-xs font-medium hover:bg-green-500/10 transition-colors disabled:opacity-50"
            >
              Reativar
            </button>
          )}

          {/* Impersonate */}
          <button
            onClick={() => openConfirm({
              title: "Entrar como cliente (impersonation)",
              description:
                `Você vai acessar a conta de "${tenant.tradeName ?? tenant.companyName}"` +
                `${impersonationTarget ? ` como ${impersonationTarget.name} (Diretoria)` : ""}. ` +
                `Você verá e poderá agir como esse usuário no sistema dele. ` +
                `Esta ação é registrada na auditoria. Continuar?`,
              variant: "danger",
              onConfirm: handleImpersonate,
            })}
            disabled={impersonateMut.isPending}
            className="flex items-center gap-1.5 h-8 px-3 rounded-lg bg-primary text-primary-foreground text-xs font-medium hover:bg-primary/90 transition-colors disabled:opacity-50"
          >
            <ExternalLink className="h-3.5 w-3.5" />
            {impersonateMut.isPending ? "Entrando..." : "Entrar como cliente"}
          </button>
        </div>
      </div>

      {/* Tabs */}
      <div className="border-b border-border">
        <div className="flex gap-1">
          {TABS.map(t => (
            <button
              key={t}
              onClick={() => setTab(t)}
              className={cn(
                "px-4 py-2.5 text-sm font-medium transition-colors border-b-2 -mb-px",
                tab === t
                  ? "border-primary text-primary"
                  : "border-transparent text-muted-foreground hover:text-foreground"
              )}
            >
              {t}
            </button>
          ))}
        </div>
      </div>

      {/* ── Tab: Geral ────────────────────────────────────────────────────────── */}
      {tab === "Geral" && (
        <div className="bg-card border border-border rounded-lg p-5 space-y-4">
          <div className="flex items-center justify-between mb-1">
            <h2 className="text-sm font-medium text-foreground">Informações da empresa</h2>
            {!editing ? (
              <button onClick={startEdit}
                className="flex items-center gap-1.5 h-7 px-3 rounded-md border border-border text-xs text-muted-foreground hover:text-foreground hover:bg-muted transition-colors">
                <Edit2 className="h-3 w-3" /> Editar
              </button>
            ) : (
              <div className="flex gap-2">
                <button onClick={() => setEditing(false)}
                  className="h-7 px-3 rounded-md border border-border text-xs text-muted-foreground hover:bg-muted transition-colors">
                  Cancelar
                </button>
                <button onClick={saveEdit} disabled={updateMut.isPending}
                  className="flex items-center gap-1.5 h-7 px-3 rounded-md bg-primary text-primary-foreground text-xs font-medium hover:bg-primary/90 transition-colors disabled:opacity-50">
                  <Save className="h-3 w-3" />
                  {updateMut.isPending ? "Salvando..." : "Salvar"}
                </button>
              </div>
            )}
          </div>

          <div className="grid grid-cols-2 gap-4">
            {editing ? (
              <>
                <InfoField label="Razão social">
                  <input className={editInput()} value={editForm.companyName}
                    onChange={e => setEditForm(f => ({ ...f, companyName: e.target.value }))} />
                </InfoField>
                <InfoField label="Nome fantasia">
                  <input className={editInput()} value={editForm.tradeName}
                    onChange={e => setEditForm(f => ({ ...f, tradeName: e.target.value }))} />
                </InfoField>
                <InfoField label="CNPJ / CPF">
                  <input className={editInput()} value={editForm.taxId}
                    onChange={e => setEditForm(f => ({ ...f, taxId: e.target.value }))} />
                </InfoField>
                <InfoField label="Telefone">
                  <input className={editInput()} value={editForm.phone}
                    onChange={e => setEditForm(f => ({ ...f, phone: e.target.value }))} />
                </InfoField>
                <InfoField label="E-mail" className="col-span-2">
                  <input className={editInput()} value={editForm.email}
                    onChange={e => setEditForm(f => ({ ...f, email: e.target.value }))} />
                </InfoField>
              </>
            ) : (
              <>
                <InfoRow label="Razão social"   value={tenant.companyName} />
                <InfoRow label="Nome fantasia"  value={tenant.tradeName ?? "—"} />
                <InfoRow label="CNPJ / CPF"     value={tenant.taxId} />
                <InfoRow label="Telefone"       value={tenant.phone ?? "—"} />
                <InfoRow label="E-mail"         value={tenant.email} />
                <InfoRow label="Segmento"       value={tenant.businessType ?? "—"} />
                <InfoRow label="Cadastrado em"  value={new Date(tenant.createdAt).toLocaleDateString("pt-BR")} />
                <InfoRow label="Slug"           value={tenant.slug} mono />
              </>
            )}
          </div>
        </div>
      )}

      {/* ── Tab: Módulos ─────────────────────────────────────────────────────── */}
      {tab === "Módulos" && (
        <div className="space-y-3">
          {AVAILABLE_MODULES.map(mod => {
            const sub = tenant.subscriptions.find(s => s.moduleKey === mod.key);
            const isActive = activeModuleKeys.includes(mod.key);

            return (
              <div key={mod.key} className="bg-card border border-border rounded-lg p-4 flex items-center justify-between">
                <div>
                  <p className="text-sm font-medium text-foreground">{mod.label}</p>
                  {sub && (
                    <p className="text-xs text-muted-foreground mt-0.5">
                      {sub.planType} · {sub.currentPeriodEnd
                        ? `Expira ${new Date(sub.currentPeriodEnd).toLocaleDateString("pt-BR")}`
                        : "Sem expiração"}
                    </p>
                  )}
                </div>
                <div className="flex items-center gap-3">
                  <span className={cn(
                    "px-2 py-0.5 rounded text-[10px] font-medium",
                    isActive ? "bg-green-500/10 text-green-600" : "bg-muted text-muted-foreground"
                  )}>
                    {isActive ? "Ativo" : "Inativo"}
                  </span>
                  {isActive ? (
                    <button
                      onClick={() => openConfirm({
                        title: "Revogar módulo",
                        description: `Revogar o módulo "${mod.label}" remove imediatamente o acesso deste cliente a esse módulo. Confirmar?`,
                        variant: "danger",
                        onConfirm: () => revokeMut.mutate(mod.key),
                      })}
                      disabled={revokeMut.isPending}
                      className="flex items-center gap-1 h-7 px-3 rounded-md border border-destructive/50 text-destructive text-xs hover:bg-destructive/10 transition-colors disabled:opacity-50"
                    >
                      <X className="h-3 w-3" /> Revogar
                    </button>
                  ) : (
                    <button
                      onClick={() => grantMut.mutate({ moduleKey: mod.key })}
                      disabled={grantMut.isPending}
                      className="flex items-center gap-1 h-7 px-3 rounded-md border border-green-500/50 text-green-600 text-xs hover:bg-green-500/10 transition-colors disabled:opacity-50"
                    >
                      <Check className="h-3 w-3" /> Ativar
                    </button>
                  )}
                </div>
              </div>
            );
          })}
        </div>
      )}

      {/* ── Tab: Histórico ───────────────────────────────────────────────────── */}
      {tab === "Histórico" && (
        <div className="bg-card border border-border rounded-lg overflow-hidden">
          <div className="px-4 py-3 border-b border-border flex items-center gap-2">
            <History className="h-4 w-4 text-primary" />
            <h2 className="text-sm font-medium text-foreground">Histórico de plano</h2>
            <span className="ml-auto text-xs text-muted-foreground">{planHistory.length} evento{planHistory.length !== 1 ? "s" : ""}</span>
          </div>
          {historyLoading ? (
            <div className="p-6 text-center text-sm text-muted-foreground">Carregando...</div>
          ) : planHistory.length === 0 ? (
            <div className="p-8 text-center text-sm text-muted-foreground">
              Nenhum evento registrado ainda.<br />
              <span className="text-xs">Eventos são criados ao ativar ou revogar módulos.</span>
            </div>
          ) : (
            <div className="divide-y divide-border">
              {planHistory.map(evt => (
                <div key={evt.id} className="flex items-start gap-3 px-4 py-3">
                  {/* Event type badge */}
                  <span className={cn(
                    "mt-0.5 px-2 py-0.5 rounded text-[10px] font-medium whitespace-nowrap shrink-0",
                    evt.eventType === "granted"      ? "bg-green-500/10 text-green-700" :
                    evt.eventType === "renewed"      ? "bg-blue-500/10 text-blue-700" :
                    evt.eventType === "revoked"      ? "bg-destructive/10 text-destructive" :
                    evt.eventType === "plan_changed" ? "bg-amber-500/10 text-amber-700" :
                    "bg-muted text-muted-foreground"
                  )}>
                    {evt.eventType === "granted"      ? "Ativado" :
                     evt.eventType === "renewed"      ? "Renovado" :
                     evt.eventType === "revoked"      ? "Revogado" :
                     evt.eventType === "plan_changed" ? "Plano alterado" :
                     evt.eventType}
                  </span>

                  {/* Details */}
                  <div className="flex-1 min-w-0">
                    <p className="text-sm text-foreground font-medium">
                      {MODULE_LABELS[evt.moduleKey] ?? evt.moduleKey}
                      {evt.planType && (
                        <span className="text-muted-foreground font-normal"> · {evt.planType}</span>
                      )}
                    </p>
                    <div className="flex items-center gap-3 mt-0.5 flex-wrap">
                      {evt.periodEnd && (
                        <span className="text-xs text-muted-foreground">
                          Expira: {new Date(evt.periodEnd).toLocaleDateString("pt-BR")}
                        </span>
                      )}
                      {evt.notes && (
                        <span className="text-xs text-muted-foreground italic truncate max-w-[240px]">
                          {evt.notes}
                        </span>
                      )}
                    </div>
                  </div>

                  {/* Date */}
                  <span className="text-xs text-muted-foreground shrink-0 tabular-nums">
                    {new Date(evt.createdAt).toLocaleString("pt-BR", { day: "2-digit", month: "2-digit", year: "2-digit", hour: "2-digit", minute: "2-digit" })}
                  </span>
                </div>
              ))}
            </div>
          )}
        </div>
      )}

      {/* ── Tab: Usuários ────────────────────────────────────────────────────── */}
      {tab === "Usuários" && (
        <div className="space-y-3">
          {/* Reset password inline panel */}
          {resetTarget && (
            <div className="bg-amber-500/5 border border-amber-500/30 rounded-lg p-4 space-y-3">
              <div className="flex items-center justify-between">
                <p className="text-sm font-medium text-foreground">
                  Redefinir senha — <span className="text-muted-foreground font-normal">{resetTarget.name}</span>
                </p>
                <button
                  onClick={() => { setResetTarget(null); setNewPassword(""); }}
                  className="p-1 rounded hover:bg-muted transition-colors"
                >
                  <X className="h-3.5 w-3.5 text-muted-foreground" />
                </button>
              </div>
              <div className="flex items-center gap-2">
                <input
                  type="password"
                  placeholder="Nova senha..."
                  value={newPassword}
                  onChange={e => setNewPassword(e.target.value)}
                  className="flex-1 h-8 px-3 rounded-md border border-border bg-background text-sm focus:outline-none focus:ring-2 focus:ring-primary/20"
                />
                <button
                  disabled={!newPassword || resetPwMut.isPending}
                  onClick={async () => {
                    await resetPwMut.mutateAsync({ userId: resetTarget.id, newPassword });
                    setResetTarget(null);
                    setNewPassword("");
                  }}
                  className="h-8 px-3 rounded-md bg-amber-500 text-white text-xs font-medium hover:bg-amber-600 disabled:opacity-50 transition-colors"
                >
                  {resetPwMut.isPending ? "Salvando..." : "Confirmar"}
                </button>
              </div>
              <p className="text-xs text-muted-foreground">
                O usuário será obrigado a trocar a senha no próximo login.
              </p>
            </div>
          )}

          <div className="bg-card border border-border rounded-lg overflow-hidden">
            <div className="px-4 py-3 border-b border-border flex items-center justify-between">
              <div className="flex items-center gap-2">
                <Users className="h-4 w-4 text-primary" />
                <h2 className="text-sm font-medium text-foreground">Usuários</h2>
              </div>
              <span className="text-xs text-muted-foreground">{tenant.users.length} usuário{tenant.users.length !== 1 ? "s" : ""}</span>
            </div>
            {tenant.users.length === 0 ? (
              <div className="p-6 text-center text-sm text-muted-foreground">Nenhum usuário cadastrado.</div>
            ) : (
              <table className="w-full text-sm">
                <thead>
                  <tr className="border-b border-border bg-muted/30">
                    <th className="text-left px-4 py-2 text-xs font-medium text-muted-foreground">Nome</th>
                    <th className="text-left px-4 py-2 text-xs font-medium text-muted-foreground">Login</th>
                    <th className="text-left px-4 py-2 text-xs font-medium text-muted-foreground">Role</th>
                    <th className="text-left px-4 py-2 text-xs font-medium text-muted-foreground">Status</th>
                    <th className="text-left px-4 py-2 text-xs font-medium text-muted-foreground">Último acesso</th>
                    <th className="text-right px-4 py-2 text-xs font-medium text-muted-foreground">Ações</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-border">
                  {tenant.users.map(u => (
                    <Fragment key={u.id}>
                    <tr className="hover:bg-muted/20 transition-colors">
                      <td className="px-4 py-3">
                        <p className="font-medium text-foreground">{u.name}</p>
                        <p className="text-xs text-muted-foreground">{u.email}</p>
                      </td>
                      <td className="px-4 py-3 text-muted-foreground font-mono text-xs">{u.login}</td>
                      <td className="px-4 py-3">
                        <span className={cn(
                          "px-1.5 py-0.5 rounded text-[10px] font-medium",
                          ROLE_COLORS[u.role] ?? "bg-muted text-muted-foreground"
                        )}>
                          {u.role}
                        </span>
                      </td>
                      <td className="px-4 py-3">
                        <span className={cn(
                          "px-1.5 py-0.5 rounded text-[10px] font-medium",
                          u.status === "Active" ? "bg-green-500/10 text-green-600" : "bg-muted text-muted-foreground"
                        )}>
                          {u.status === "Active" ? "Ativo" : u.status}
                        </span>
                      </td>
                      <td className="px-4 py-3 text-xs text-muted-foreground">
                        {u.lastAccessAt ? new Date(u.lastAccessAt).toLocaleString("pt-BR") : "—"}
                      </td>
                      <td className="px-4 py-3">
                        <div className="flex items-center justify-end gap-1.5">
                          <button
                            title="Redefinir senha"
                            onClick={() => { setResetTarget({ id: u.id, name: u.name }); setNewPassword(""); }}
                            className="flex items-center gap-1 h-7 px-2 rounded-md border border-amber-500/40 text-amber-600 text-xs hover:bg-amber-500/10 transition-colors"
                          >
                            <KeyRound className="h-3 w-3" /> Senha
                          </button>
                          <button
                            title="Revogar todas sessões"
                            disabled={revokeSessionsMut.isPending}
                            onClick={() => openConfirm({
                              title: "Revogar sessões",
                              description: `Isso vai desconectar ${u.name} imediatamente e invalidar todos os tokens ativos. Confirmar?`,
                              variant: "danger",
                              onConfirm: () => revokeSessionsMut.mutate(u.id),
                            })}
                            className="flex items-center gap-1 h-7 px-2 rounded-md border border-destructive/40 text-destructive text-xs hover:bg-destructive/10 transition-colors disabled:opacity-50"
                          >
                            <LogOutIcon className="h-3 w-3" /> Logout
                          </button>
                          <button
                            title={expandedUser === u.id ? "Ocultar sessões" : "Ver sessões"}
                            onClick={() => setExpandedUser(v => v === u.id ? null : u.id)}
                            className="flex items-center gap-1 h-7 px-2 rounded-md border border-border text-muted-foreground text-xs hover:bg-muted transition-colors"
                          >
                            <Monitor className="h-3 w-3" />
                            {expandedUser === u.id ? <ChevronUp className="h-3 w-3" /> : <ChevronDown className="h-3 w-3" />}
                          </button>
                        </div>
                      </td>
                    </tr>
                    {expandedUser === u.id && (
                      <SessionsRow
                        userId={u.id}
                        tenantId={tenantId!}
                        colSpan={6}
                      />
                    )}
                    </Fragment>
                  ))}
                </tbody>
              </table>
            )}
          </div>
        </div>
      )}

      {/* ── Tab: Notas ───────────────────────────────────────────────────────── */}
      {tab === "Notas" && (
        <div className="space-y-4">
          {/* Compose */}
          <div className="bg-card border border-border rounded-lg p-4 space-y-3">
            <div className="flex items-center gap-2">
              <StickyNote className="h-4 w-4 text-primary" />
              <h2 className="text-sm font-medium text-foreground">Nova nota</h2>
            </div>
            <textarea
              rows={3}
              placeholder="Escreva uma nota interna sobre este cliente..."
              value={noteText}
              onChange={e => setNoteText(e.target.value)}
              className="w-full px-3 py-2 rounded-md border border-border bg-muted/20 text-sm resize-none focus:outline-none focus:ring-2 focus:ring-primary/20"
            />
            <div className="flex items-center justify-between">
              <label className="flex items-center gap-2 text-xs text-muted-foreground cursor-pointer select-none">
                <input
                  type="checkbox"
                  checked={notePinned}
                  onChange={e => setNotePinned(e.target.checked)}
                  className="rounded"
                />
                Fixar nota
              </label>
              <button
                disabled={!noteText.trim() || createNoteMut.isPending}
                onClick={async () => {
                  await createNoteMut.mutateAsync({ content: noteText, isPinned: notePinned });
                  setNoteText("");
                  setNotePinned(false);
                }}
                className="h-8 px-4 rounded-md bg-primary text-primary-foreground text-xs font-medium hover:bg-primary/90 disabled:opacity-50 transition-colors"
              >
                {createNoteMut.isPending ? "Salvando..." : "Adicionar nota"}
              </button>
            </div>
          </div>

          {/* Notes list */}
          {notes.length === 0 ? (
            <div className="p-8 text-center text-sm text-muted-foreground border border-dashed border-border rounded-lg">
              Nenhuma nota ainda. Adicione contexto, histórico ou lembretes sobre este cliente.
            </div>
          ) : (
            <div className="space-y-2">
              {/* Pinned first */}
              {[...notes].sort((a, b) => (b.isPinned ? 1 : 0) - (a.isPinned ? 1 : 0)).map(note => (
                <div
                  key={note.id}
                  className={cn(
                    "bg-card border rounded-lg p-4",
                    note.isPinned ? "border-primary/30 bg-primary/5" : "border-border"
                  )}
                >
                  <div className="flex items-start justify-between gap-3">
                    <p className="text-sm text-foreground flex-1 whitespace-pre-wrap">{note.content}</p>
                    <div className="flex items-center gap-1 shrink-0">
                      <button
                        title={note.isPinned ? "Desafixar" : "Fixar"}
                        onClick={() => togglePinMut.mutate(note.id)}
                        disabled={togglePinMut.isPending}
                        className={cn(
                          "p-1.5 rounded hover:bg-muted transition-colors disabled:opacity-50",
                          note.isPinned ? "text-primary" : "text-muted-foreground"
                        )}
                      >
                        {note.isPinned ? <Pin className="h-3.5 w-3.5" /> : <PinOff className="h-3.5 w-3.5" />}
                      </button>
                      <button
                        title="Excluir nota"
                        onClick={() => openConfirm({
                          title: "Excluir nota",
                          description: "Esta nota será removida permanentemente.",
                          variant: "danger",
                          onConfirm: () => deleteNoteMut.mutate(note.id),
                        })}
                        disabled={deleteNoteMut.isPending}
                        className="p-1.5 rounded text-muted-foreground hover:text-destructive hover:bg-muted transition-colors disabled:opacity-50"
                      >
                        <Trash2 className="h-3.5 w-3.5" />
                      </button>
                    </div>
                  </div>
                  <div className="flex items-center gap-2 mt-2">
                    {note.isPinned && (
                      <span className="flex items-center gap-0.5 text-[10px] text-primary font-medium">
                        <Pin className="h-2.5 w-2.5" /> Fixada
                      </span>
                    )}
                    <span className="text-[10px] text-muted-foreground">
                      {note.authorName} · {new Date(note.createdAt).toLocaleString("pt-BR")}
                    </span>
                  </div>
                </div>
              ))}
            </div>
          )}
        </div>
      )}

      {/* ── Tab: Lojas ───────────────────────────────────────────────────────── */}
      {tab === "Lojas" && (
        <div className="bg-card border border-border rounded-lg overflow-hidden">
          <div className="px-4 py-3 border-b border-border flex items-center justify-between">
            <div className="flex items-center gap-2">
              <Store className="h-4 w-4 text-primary" />
              <h2 className="text-sm font-medium text-foreground">Lojas / Filiais</h2>
            </div>
            <span className="text-xs text-muted-foreground">{tenant.stores.length} loja{tenant.stores.length !== 1 ? "s" : ""}</span>
          </div>
          {tenant.stores.length === 0 ? (
            <div className="p-6 text-center text-sm text-muted-foreground">Nenhuma loja cadastrada.</div>
          ) : (
            <div className="divide-y divide-border">
              {tenant.stores.map(s => (
                <div key={s.id} className="flex items-center justify-between px-4 py-3">
                  <div>
                    <p className="text-sm font-medium text-foreground">{s.name}</p>
                    <p className="text-xs text-muted-foreground font-mono">{s.slug}</p>
                  </div>
                  <span className={cn(
                    "px-2 py-0.5 rounded text-[10px] font-medium",
                    s.status === "Active" ? "bg-green-500/10 text-green-600" : "bg-muted text-muted-foreground"
                  )}>
                    {s.status === "Active" ? "Ativa" : s.status}
                  </span>
                </div>
              ))}
            </div>
          )}
        </div>
      )}

    </div>
  );
}

// ─── Helpers ─────────────────────────────────────────────────────────────────

function StatusBadge({ status }: { status: string }) {
  const cfg =
    status === "Active"    ? "bg-green-500/10 text-green-600" :
    status === "Suspended" ? "bg-amber-500/10 text-amber-600" :
    "bg-muted text-muted-foreground";
  const label =
    status === "Active" ? "Ativo" : status === "Suspended" ? "Suspenso" : status;
  return <span className={cn("px-2 py-0.5 rounded text-xs font-medium", cfg)}>{label}</span>;
}

function InfoRow({ label, value, mono, className }: {
  label: string; value: string; mono?: boolean; className?: string;
}) {
  return (
    <div className={className}>
      <p className="text-xs text-muted-foreground mb-0.5">{label}</p>
      <p className={cn("text-sm text-foreground", mono && "font-mono text-xs")}>{value}</p>
    </div>
  );
}

function InfoField({ label, children, className }: {
  label: string; children: React.ReactNode; className?: string;
}) {
  return (
    <div className={className}>
      <label className="block text-xs text-muted-foreground mb-1">{label}</label>
      {children}
    </div>
  );
}

function editInput() {
  return "w-full h-8 px-2.5 rounded-md border border-border bg-muted/30 text-sm focus:outline-none focus:ring-2 focus:ring-primary/20";
}

// ─── Sessions expandable row ──────────────────────────────────────────────────

function SessionsRow({ userId, tenantId, colSpan }: {
  userId: string; tenantId: string; colSpan: number;
}) {
  const { data: sessions = [], isLoading } = useUserSessions(tenantId, userId, true);

  return (
    <tr className="bg-muted/10">
      <td colSpan={colSpan} className="px-6 py-3">
        {isLoading ? (
          <p className="text-xs text-muted-foreground">Carregando sessões...</p>
        ) : sessions.length === 0 ? (
          <p className="text-xs text-muted-foreground">Nenhuma sessão ativa.</p>
        ) : (
          <div className="space-y-1.5">
            <p className="text-[10px] font-medium text-muted-foreground uppercase tracking-wide mb-2">
              {sessions.length} sessão{sessions.length !== 1 ? "ões" : ""} ativa{sessions.length !== 1 ? "s" : ""}
            </p>
            {sessions.map(s => (
              <div key={s.id} className="flex items-center gap-3 text-xs text-muted-foreground bg-background border border-border rounded-md px-3 py-2">
                <Wifi className="h-3 w-3 shrink-0 text-green-500" />
                <span className="font-mono text-foreground">{s.ipAddress ?? "IP desconhecido"}</span>
                <span className="flex-1 truncate">{parseUserAgent(s.userAgent)}</span>
                <span className="shrink-0 text-[10px]">
                  Último uso: {new Date(s.lastUsedAt).toLocaleString("pt-BR")}
                </span>
              </div>
            ))}
          </div>
        )}
      </td>
    </tr>
  );
}

function parseUserAgent(ua: string | null): string {
  if (!ua) return "Dispositivo desconhecido";
  if (ua.includes("Mobile")) return "Mobile";
  if (ua.includes("Chrome"))  return "Chrome";
  if (ua.includes("Firefox")) return "Firefox";
  if (ua.includes("Safari"))  return "Safari";
  return ua.length > 60 ? ua.slice(0, 60) + "…" : ua;
}

