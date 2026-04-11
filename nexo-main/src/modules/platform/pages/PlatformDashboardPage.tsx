import { Building2, Store, Users, Package } from "lucide-react";
import { usePlatformTenants } from "../hooks/usePlatformTenants";

export default function PlatformDashboardPage() {
  const { data: tenants, isLoading } = usePlatformTenants();

  const totalTenants = tenants?.length ?? 0;
  const totalStores  = tenants?.reduce((acc, t) => acc + t.stores.length, 0) ?? 0;
  const totalUsers   = tenants?.reduce((acc, t) => acc + t.userCount, 0) ?? 0;
  const activeCount  = tenants?.filter(t => t.status === "Active").length ?? 0;

  return (
    <div className="p-6 space-y-6">
      <div>
        <h1 className="text-xl font-semibold text-foreground">Dashboard</h1>
        <p className="text-sm text-muted-foreground mt-0.5">Visão geral da plataforma NexoERP</p>
      </div>

      {/* Stats */}
      <div className="grid grid-cols-2 lg:grid-cols-4 gap-4">
        {[
          { label: "Clientes ativos",  value: isLoading ? "—" : activeCount,   icon: Building2, color: "text-primary" },
          { label: "Total de clientes", value: isLoading ? "—" : totalTenants,  icon: Building2, color: "text-muted-foreground" },
          { label: "Lojas / Filiais",   value: isLoading ? "—" : totalStores,   icon: Store,     color: "text-secondary" },
          { label: "Usuários totais",   value: isLoading ? "—" : totalUsers,    icon: Users,     color: "text-accent-foreground" },
        ].map(({ label, value, icon: Icon, color }) => (
          <div key={label} className="bg-card border border-border rounded-lg p-4">
            <div className="flex items-center justify-between mb-2">
              <p className="text-xs text-muted-foreground">{label}</p>
              <Icon className={`h-4 w-4 ${color}`} />
            </div>
            <p className="text-2xl font-semibold text-foreground">{value}</p>
          </div>
        ))}
      </div>

      {/* Quick tenant list */}
      <div className="bg-card border border-border rounded-lg">
        <div className="px-4 py-3 border-b border-border">
          <h2 className="text-sm font-medium text-foreground">Clientes recentes</h2>
        </div>
        {isLoading ? (
          <div className="p-6 text-center text-sm text-muted-foreground">Carregando...</div>
        ) : (
          <div className="divide-y divide-border">
            {tenants?.slice(0, 5).map(t => (
              <div key={t.id} className="flex items-center justify-between px-4 py-3">
                <div>
                  <p className="text-sm font-medium text-foreground">{t.tradeName ?? t.companyName}</p>
                  <p className="text-xs text-muted-foreground">{t.modules.join(", ") || "sem módulo"}</p>
                </div>
                <div className="flex items-center gap-4 text-xs text-muted-foreground">
                  <span>{t.stores.length} loja{t.stores.length !== 1 ? "s" : ""}</span>
                  <span>{t.userCount} usuário{t.userCount !== 1 ? "s" : ""}</span>
                  <span className={`px-1.5 py-0.5 rounded text-[10px] font-medium ${
                    t.status === "Active" ? "bg-green-500/10 text-green-600" : "bg-muted text-muted-foreground"
                  }`}>{t.status}</span>
                </div>
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  );
}
