import { useState } from "react";
import { useNavigate } from "react-router-dom";
import { Building2, Store, Users, ChevronRight, Search, Plus } from "lucide-react";
import { usePlatformTenants } from "../hooks/usePlatformTenants";
import { CreateTenantDrawer } from "../components/CreateTenantDrawer";

const MODULE_LABELS: Record<string, string> = {
  varejo:              "Varejo",
  restaurante:         "Restaurante",
  "academia-musculacao": "Academia",
  "salao-beleza":      "Salão",
  "pet-shop":          "Pet Shop",
  "clinica-medica":    "Clínica",
  "oficina-mecanica":  "Oficina",
  "pousada-hotel":     "Hotel",
  imobiliaria:         "Imobiliária",
};

export default function PlatformTenantsPage() {
  const { data: tenants, isLoading } = usePlatformTenants();
  const navigate = useNavigate();
  const [search, setSearch] = useState("");
  const [drawerOpen, setDrawerOpen] = useState(false);

  const filtered = tenants?.filter(t =>
    t.companyName.toLowerCase().includes(search.toLowerCase()) ||
    (t.tradeName ?? "").toLowerCase().includes(search.toLowerCase()) ||
    t.email.toLowerCase().includes(search.toLowerCase())
  ) ?? [];

  return (
    <>
      <div className="p-6 space-y-4">
        <div className="flex items-center justify-between">
          <div>
            <h1 className="font-display text-[20px] font-bold text-foreground tracking-tight">Clientes</h1>
            <p className="text-sm text-muted-foreground mt-0.5">
              {isLoading ? "Carregando..." : `${tenants?.length ?? 0} empresas cadastradas`}
            </p>
          </div>
          <button
            onClick={() => setDrawerOpen(true)}
            className="flex items-center gap-2 h-9 px-4 rounded-lg bg-primary text-primary-foreground text-sm font-medium hover:bg-primary/90 transition-colors"
          >
            <Plus className="h-4 w-4" />
            Novo cliente
          </button>
        </div>

        {/* Search */}
        <div className="relative max-w-sm">
          <Search className="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground" />
          <input
            type="text"
            placeholder="Buscar por empresa, email..."
            value={search}
            onChange={e => setSearch(e.target.value)}
            className="w-full h-9 pl-9 pr-3 rounded-lg bg-muted border-none text-sm placeholder:text-muted-foreground focus:outline-none focus:ring-2 focus:ring-primary/20"
          />
        </div>

        {/* Table */}
        <div className="bg-card border border-border rounded-lg overflow-hidden">
          <table className="w-full text-sm">
            <thead>
              <tr className="border-b border-border bg-muted/50">
                <th className="text-left px-4 py-2.5 text-xs font-medium text-muted-foreground">Empresa</th>
                <th className="text-left px-4 py-2.5 text-xs font-medium text-muted-foreground">Módulos</th>
                <th className="text-left px-4 py-2.5 text-xs font-medium text-muted-foreground">Lojas</th>
                <th className="text-left px-4 py-2.5 text-xs font-medium text-muted-foreground">Usuários</th>
                <th className="text-left px-4 py-2.5 text-xs font-medium text-muted-foreground">Status</th>
                <th className="px-4 py-2.5" />
              </tr>
            </thead>
            <tbody className="divide-y divide-border">
              {isLoading && (
                <tr>
                  <td colSpan={6} className="px-4 py-8 text-center text-muted-foreground">Carregando...</td>
                </tr>
              )}
              {!isLoading && filtered.length === 0 && (
                <tr>
                  <td colSpan={6} className="px-4 py-8 text-center text-muted-foreground">
                    {search ? "Nenhum cliente encontrado." : "Nenhum cliente cadastrado ainda."}
                  </td>
                </tr>
              )}
              {filtered.map(t => (
                <tr
                  key={t.id}
                  onClick={() => navigate(`/platform/tenants/${t.id}`)}
                  className="hover:bg-muted/50 cursor-pointer transition-colors"
                >
                  <td className="px-4 py-3">
                    <p className="font-medium text-foreground">{t.tradeName ?? t.companyName}</p>
                    <p className="text-xs text-muted-foreground">{t.email}</p>
                  </td>
                  <td className="px-4 py-3">
                    <div className="flex flex-wrap gap-1">
                      {t.modules.length === 0 ? (
                        <span className="text-xs text-muted-foreground">—</span>
                      ) : t.modules.map(m => (
                        <span key={m} className="px-1.5 py-0.5 bg-primary/10 text-primary text-[10px] font-medium rounded">
                          {MODULE_LABELS[m] ?? m}
                        </span>
                      ))}
                    </div>
                  </td>
                  <td className="px-4 py-3">
                    <div className="flex items-center gap-1 text-muted-foreground">
                      <Store className="h-3.5 w-3.5" />
                      <span>{t.stores.length}</span>
                    </div>
                  </td>
                  <td className="px-4 py-3">
                    <div className="flex items-center gap-1 text-muted-foreground">
                      <Users className="h-3.5 w-3.5" />
                      <span>{t.userCount}</span>
                    </div>
                  </td>
                  <td className="px-4 py-3">
                    <span className={`px-2 py-0.5 rounded text-[10px] font-medium ${
                      t.status === "Active"
                        ? "bg-green-500/10 text-green-600"
                        : t.status === "Suspended"
                        ? "bg-amber-500/10 text-amber-600"
                        : "bg-muted text-muted-foreground"
                    }`}>
                      {t.status === "Active" ? "Ativo" : t.status === "Suspended" ? "Suspenso" : t.status}
                    </span>
                  </td>
                  <td className="px-4 py-3 text-right">
                    <ChevronRight className="h-4 w-4 text-muted-foreground inline" />
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </div>

      <CreateTenantDrawer
        open={drawerOpen}
        onClose={() => setDrawerOpen(false)}
        onCreated={(id) => navigate(`/platform/tenants/${id}`)}
      />
    </>
  );
}
