import { useParams, useNavigate } from "react-router-dom";
import { ArrowLeft, Store, Users, Package } from "lucide-react";
import { usePlatformTenant } from "../hooks/usePlatformTenants";

const ROLE_LABELS: Record<string, string> = {
  Diretoria: "Diretoria", Gerente: "Gerente", Vendedor: "Vendedor", Estoquista: "Estoquista",
};

export default function PlatformTenantDetailPage() {
  const { tenantId } = useParams<{ tenantId: string }>();
  const navigate = useNavigate();
  const { data: tenant, isLoading } = usePlatformTenant(tenantId ?? "");

  if (isLoading) {
    return <div className="p-6 text-sm text-muted-foreground">Carregando...</div>;
  }

  if (!tenant) {
    return <div className="p-6 text-sm text-muted-foreground">Tenant não encontrado.</div>;
  }

  return (
    <div className="p-6 space-y-6">
      {/* Header */}
      <div className="flex items-center gap-3">
        <button
          onClick={() => navigate("/platform/tenants")}
          className="p-1.5 rounded-md hover:bg-muted transition-colors"
        >
          <ArrowLeft className="h-4 w-4 text-muted-foreground" />
        </button>
        <div>
          <h1 className="text-xl font-semibold text-foreground">
            {tenant.tradeName ?? tenant.companyName}
          </h1>
          <p className="text-sm text-muted-foreground">{tenant.email} · {tenant.taxId}</p>
        </div>
        <span className={`ml-auto px-2 py-0.5 rounded text-xs font-medium ${
          tenant.status === "Active" ? "bg-green-500/10 text-green-600" : "bg-muted text-muted-foreground"
        }`}>
          {tenant.status === "Active" ? "Ativo" : tenant.status}
        </span>
      </div>

      <div className="grid grid-cols-1 lg:grid-cols-3 gap-4">
        {/* Módulos */}
        <div className="bg-card border border-border rounded-lg p-4 space-y-2">
          <div className="flex items-center gap-2 mb-3">
            <Package className="h-4 w-4 text-primary" />
            <h2 className="text-sm font-medium text-foreground">Módulos</h2>
          </div>
          {tenant.subscriptions.length === 0 ? (
            <p className="text-xs text-muted-foreground">Nenhum módulo ativo.</p>
          ) : tenant.subscriptions.map(s => (
            <div key={s.id} className="flex items-center justify-between">
              <span className="text-sm text-foreground">{s.moduleKey}</span>
              <span className={`text-[10px] font-medium px-1.5 py-0.5 rounded ${
                s.status === "Active" ? "bg-green-500/10 text-green-600" : "bg-muted text-muted-foreground"
              }`}>{s.status}</span>
            </div>
          ))}
        </div>

        {/* Lojas */}
        <div className="bg-card border border-border rounded-lg p-4 space-y-2">
          <div className="flex items-center gap-2 mb-3">
            <Store className="h-4 w-4 text-secondary" />
            <h2 className="text-sm font-medium text-foreground">Lojas / Filiais</h2>
            <span className="ml-auto text-xs text-muted-foreground">{tenant.stores.length}</span>
          </div>
          {tenant.stores.length === 0 ? (
            <p className="text-xs text-muted-foreground">Nenhuma loja cadastrada.</p>
          ) : tenant.stores.map(s => (
            <div key={s.id} className="flex items-center justify-between py-1">
              <span className="text-sm text-foreground">{s.name}</span>
              <span className={`text-[10px] font-medium px-1.5 py-0.5 rounded ${
                s.status === "Active" ? "bg-green-500/10 text-green-600" : "bg-muted text-muted-foreground"
              }`}>{s.status === "Active" ? "Ativa" : s.status}</span>
            </div>
          ))}
        </div>

        {/* Usuários */}
        <div className="bg-card border border-border rounded-lg p-4 space-y-2">
          <div className="flex items-center gap-2 mb-3">
            <Users className="h-4 w-4 text-accent-foreground" />
            <h2 className="text-sm font-medium text-foreground">Usuários</h2>
            <span className="ml-auto text-xs text-muted-foreground">{tenant.users.length}</span>
          </div>
          {tenant.users.length === 0 ? (
            <p className="text-xs text-muted-foreground">Nenhum usuário.</p>
          ) : tenant.users.map(u => (
            <div key={u.id} className="flex items-center justify-between py-1">
              <div>
                <p className="text-sm text-foreground">{u.name}</p>
                <p className="text-[10px] text-muted-foreground">{u.login}</p>
              </div>
              <span className="text-[10px] text-muted-foreground">{ROLE_LABELS[u.role] ?? u.role}</span>
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}
