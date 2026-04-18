import { useState } from "react";
import { useParams, useNavigate } from "react-router-dom";
import {
  ArrowLeft, Store, Users, Package, ExternalLink,
  Check, X, AlertTriangle, Edit2, Save,
} from "lucide-react";
import { cn } from "@/lib/utils";

import {
  usePlatformTenant,
  useSetTenantStatus,
  useUpdateTenant,
  useGrantModule,
  useRevokeModule,
  useImpersonate,
} from "../hooks/usePlatformTenants";

// ─── Constants ────────────────────────────────────────────────────────────────

const TABS = ["Geral", "Módulos", "Usuários", "Lojas"] as const;
type Tab = typeof TABS[number];

const MODULE_LABELS: Record<string, string> = {
  varejo:      "Varejo",
  restaurante: "Restaurante",
};

const AVAILABLE_MODULES = [
  { key: "varejo",      label: "Varejo" },
  { key: "restaurante", label: "Restaurante" },
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

  const setStatusMut  = useSetTenantStatus(tenantId ?? "");
  const updateMut     = useUpdateTenant(tenantId ?? "");
  const grantMut      = useGrantModule(tenantId ?? "");
  const revokeMut     = useRevokeModule(tenantId ?? "");
  const impersonateMut = useImpersonate();

  const [tab, setTab] = useState<Tab>("Geral");
  const [editing, setEditing] = useState(false);
  const [editForm, setEditForm] = useState({
    companyName: "", tradeName: "", taxId: "", email: "", phone: "", businessType: "",
  });

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
      alert(msg);
    }
  };

  const activeModuleKeys = tenant.subscriptions
    .filter(s => s.status === "Active")
    .map(s => s.moduleKey);

  return (
    <div className="p-6 space-y-5 max-w-4xl">

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
              onClick={() => setStatusMut.mutate("Suspended")}
              disabled={setStatusMut.isPending}
              className="h-8 px-3 rounded-lg border border-amber-500/50 text-amber-600 text-xs font-medium hover:bg-amber-500/10 transition-colors disabled:opacity-50"
            >
              Suspender
            </button>
          ) : (
            <button
              onClick={() => setStatusMut.mutate("Active")}
              disabled={setStatusMut.isPending}
              className="h-8 px-3 rounded-lg border border-green-500/50 text-green-600 text-xs font-medium hover:bg-green-500/10 transition-colors disabled:opacity-50"
            >
              Reativar
            </button>
          )}

          {/* Impersonate */}
          <button
            onClick={handleImpersonate}
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
                      onClick={() => revokeMut.mutate(mod.key)}
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

      {/* ── Tab: Usuários ────────────────────────────────────────────────────── */}
      {tab === "Usuários" && (
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
                </tr>
              </thead>
              <tbody className="divide-y divide-border">
                {tenant.users.map(u => (
                  <tr key={u.id}>
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
                  </tr>
                ))}
              </tbody>
            </table>
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

