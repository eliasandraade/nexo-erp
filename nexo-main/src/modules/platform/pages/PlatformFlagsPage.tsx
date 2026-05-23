import { useState } from "react";
import { Flag, RefreshCw, ChevronDown, ChevronUp, RotateCcw, Search } from "lucide-react";
import { cn } from "@/lib/utils";
import {
  useFlags,
  useToggleFlagDefault,
  usePlatformTenants,
  useTenantFlags,
  useSetTenantFlagOverride,
  useDeleteTenantFlagOverride,
} from "../hooks/usePlatformTenants";
import type { TenantFlagResolved } from "../services/platformApi";

// ─── Category labels ──────────────────────────────────────────────────────────

const CATEGORY_LABELS: Record<string, string> = {
  pdv:         "PDV",
  restaurante: "Restaurante",
  estoque:     "Estoque",
  financeiro:  "Financeiro",
  geral:       "Geral",
};

// ─── Toggle switch ────────────────────────────────────────────────────────────

function Toggle({
  checked,
  onChange,
  disabled,
  size = "md",
}: {
  checked: boolean;
  onChange: (v: boolean) => void;
  disabled?: boolean;
  size?: "sm" | "md";
}) {
  const track = size === "sm"
    ? "w-8 h-4 rounded-full"
    : "w-10 h-5 rounded-full";
  const thumb = size === "sm"
    ? "w-3 h-3 top-0.5 translate-x-0.5"
    : "w-4 h-4 top-0.5 translate-x-0.5";
  const translate = size === "sm" ? "translate-x-4" : "translate-x-5";

  return (
    <button
      type="button"
      role="switch"
      aria-checked={checked}
      disabled={disabled}
      onClick={() => onChange(!checked)}
      className={cn(
        "relative inline-block shrink-0 transition-colors focus:outline-none focus-visible:ring-2 focus-visible:ring-primary/40 rounded-full",
        track,
        checked ? "bg-primary" : "bg-muted-foreground/30",
        disabled && "opacity-40 cursor-not-allowed"
      )}
    >
      <span
        className={cn(
          "absolute block rounded-full bg-white shadow transition-transform",
          thumb,
          checked && translate
        )}
      />
    </button>
  );
}

// ─── Tenant overrides panel ───────────────────────────────────────────────────

function TenantFilterPanel({ tenantId }: { tenantId: string }) {
  const { data: flags = [], isLoading } = useTenantFlags(tenantId);
  const overrideMut = useSetTenantFlagOverride(tenantId);
  const deleteMut   = useDeleteTenantFlagOverride(tenantId);

  const grouped = flags.reduce<Record<string, TenantFlagResolved[]>>((acc, f) => {
    (acc[f.category] ??= []).push(f);
    return acc;
  }, {});

  if (isLoading) return (
    <div className="p-4 text-sm text-muted-foreground">Carregando flags do tenant...</div>
  );

  return (
    <div className="space-y-4">
      {Object.entries(grouped).map(([cat, items]) => (
        <div key={cat}>
          <p className="px-1 mb-2 text-[10px] font-semibold uppercase tracking-widest text-muted-foreground">
            {CATEGORY_LABELS[cat] ?? cat}
          </p>
          <div className="bg-card border border-border rounded-lg divide-y divide-border">
            {items.map(f => (
              <div key={f.key} className="flex items-center gap-3 px-4 py-3">
                <div className="flex-1 min-w-0">
                  <div className="flex items-center gap-2">
                    <p className="text-sm font-medium text-foreground truncate">{f.name}</p>
                    {f.hasOverride && (
                      <span className="px-1.5 py-0.5 rounded text-[9px] font-semibold bg-primary/10 text-primary shrink-0">
                        OVERRIDE
                      </span>
                    )}
                  </div>
                  <p className="text-[10px] text-muted-foreground font-mono mt-0.5">{f.key}</p>
                  {f.notes && (
                    <p className="text-[10px] text-muted-foreground italic mt-0.5 truncate">{f.notes}</p>
                  )}
                </div>

                {/* Global default pill */}
                <span className={cn(
                  "shrink-0 text-[10px] px-1.5 py-0.5 rounded",
                  f.defaultEnabled ? "bg-green-500/10 text-green-700" : "bg-muted text-muted-foreground"
                )}>
                  Global: {f.defaultEnabled ? "ON" : "OFF"}
                </span>

                {/* Toggle (shows resolved value) */}
                <Toggle
                  checked={f.resolved}
                  disabled={overrideMut.isPending || deleteMut.isPending}
                  onChange={(val) => overrideMut.mutate({ key: f.key, isEnabled: val })}
                />

                {/* Reset to global */}
                {f.hasOverride && (
                  <button
                    title="Remover override e usar padrão global"
                    disabled={deleteMut.isPending}
                    onClick={() => deleteMut.mutate(f.key)}
                    className="p-1.5 rounded text-muted-foreground hover:text-destructive hover:bg-muted transition-colors disabled:opacity-40"
                  >
                    <RotateCcw className="h-3 w-3" />
                  </button>
                )}
              </div>
            ))}
          </div>
        </div>
      ))}
    </div>
  );
}

// ─── Main page ────────────────────────────────────────────────────────────────

export default function PlatformFlagsPage() {
  const [searchTenant, setSearchTenant] = useState("");
  const [selectedTenantId, setSelectedTenantId] = useState<string | null>(null);
  const [categoryFilter, setCategoryFilter] = useState<string>("all");
  const [showGlobalExpanded, setShowGlobalExpanded] = useState(true);

  const { data: flags = [], isLoading, refetch, isFetching } = useFlags();
  const { data: tenants = [] } = usePlatformTenants();
  const toggleMut = useToggleFlagDefault();

  const categories = ["all", ...Array.from(new Set(flags.map(f => f.category))).sort()];

  const filteredFlags = flags.filter(f =>
    categoryFilter === "all" || f.category === categoryFilter
  );

  const grouped = filteredFlags.reduce<Record<string, typeof filteredFlags>>((acc, f) => {
    (acc[f.category] ??= []).push(f);
    return acc;
  }, {});

  const filteredTenants = tenants.filter(t =>
    (t.tradeName ?? t.companyName).toLowerCase().includes(searchTenant.toLowerCase()) ||
    t.email.toLowerCase().includes(searchTenant.toLowerCase())
  );

  return (
    <div className="p-6 space-y-5 max-w-5xl">

      {/* Header */}
      <div className="flex items-center justify-between">
        <div>
          <h1 className="font-display text-[20px] font-bold text-foreground tracking-tight">Feature Flags</h1>
          <p className="text-sm text-muted-foreground mt-0.5">
            Ligar e desligar funcionalidades sem novo deploy
          </p>
        </div>
        <button
          onClick={() => refetch()}
          disabled={isFetching}
          className="flex items-center gap-1.5 h-8 px-3 rounded-xl border border-border text-sm text-muted-foreground hover:text-foreground hover:bg-muted transition-colors disabled:opacity-50"
        >
          <RefreshCw className={cn("h-3.5 w-3.5", isFetching && "animate-spin")} />
          Atualizar
        </button>
      </div>

      {/* Two-column layout: global flags + tenant filter */}
      <div className="grid grid-cols-1 lg:grid-cols-[1fr_280px] gap-5">

        {/* ── Global flags ──────────────────────────────────────────────────── */}
        <div className="space-y-3">

          {/* Category filter */}
          <div className="flex items-center gap-1.5 flex-wrap">
            {categories.map(c => (
              <button
                key={c}
                onClick={() => setCategoryFilter(c)}
                className={cn(
                  "h-7 px-3 rounded-full text-xs font-medium transition-colors",
                  categoryFilter === c
                    ? "bg-primary text-primary-foreground"
                    : "bg-muted text-muted-foreground hover:text-foreground"
                )}
              >
                {c === "all" ? "Todas" : CATEGORY_LABELS[c] ?? c}
              </button>
            ))}
          </div>

          {/* Flags list */}
          <div className="bg-card border border-border rounded-lg overflow-hidden">
            <button
              onClick={() => setShowGlobalExpanded(v => !v)}
              className="w-full flex items-center gap-2 px-4 py-3 border-b border-border hover:bg-muted/30 transition-colors"
            >
              <Flag className="h-4 w-4 text-primary" />
              <span className="text-sm font-medium text-foreground">Padrão global</span>
              <span className="ml-auto text-xs text-muted-foreground mr-2">
                {filteredFlags.length} flag{filteredFlags.length !== 1 ? "s" : ""}
              </span>
              {showGlobalExpanded
                ? <ChevronUp className="h-4 w-4 text-muted-foreground" />
                : <ChevronDown className="h-4 w-4 text-muted-foreground" />}
            </button>

            {showGlobalExpanded && (
              isLoading ? (
                <div className="p-6 text-center text-sm text-muted-foreground">Carregando...</div>
              ) : filteredFlags.length === 0 ? (
                <div className="p-6 text-center text-sm text-muted-foreground">Nenhuma flag encontrada.</div>
              ) : (
                Object.entries(grouped).map(([cat, items]) => (
                  <div key={cat}>
                    <div className="px-4 py-2 bg-muted/20 border-b border-border">
                      <p className="text-[10px] font-semibold uppercase tracking-widest text-muted-foreground">
                        {CATEGORY_LABELS[cat] ?? cat}
                      </p>
                    </div>
                    {items.map((f, idx) => (
                      <div
                        key={f.key}
                        className={cn(
                          "flex items-center gap-4 px-4 py-3.5",
                          idx < items.length - 1 && "border-b border-border/50"
                        )}
                      >
                        {/* Info */}
                        <div className="flex-1 min-w-0">
                          <p className="text-sm font-medium text-foreground">{f.name}</p>
                          <p className="text-[10px] text-muted-foreground font-mono mt-0.5">{f.key}</p>
                          {f.description && (
                            <p className="text-xs text-muted-foreground mt-1 leading-relaxed">{f.description}</p>
                          )}
                        </div>

                        {/* Override count */}
                        {f.overrideCount > 0 && (
                          <span className="shrink-0 text-[10px] text-muted-foreground bg-muted px-1.5 py-0.5 rounded">
                            {f.overrideCount} override{f.overrideCount !== 1 ? "s" : ""}
                          </span>
                        )}

                        {/* Toggle global default */}
                        <div className="shrink-0 flex flex-col items-center gap-1">
                          <Toggle
                            checked={f.defaultEnabled}
                            disabled={toggleMut.isPending}
                            onChange={() => toggleMut.mutate(f.key)}
                          />
                          <span className="text-[9px] text-muted-foreground">
                            {f.defaultEnabled ? "ON" : "OFF"}
                          </span>
                        </div>
                      </div>
                    ))}
                  </div>
                ))
              )
            )}
          </div>
        </div>

        {/* ── Tenant-specific panel ─────────────────────────────────────────── */}
        <div className="space-y-3">
          <div className="bg-card border border-border rounded-lg">
            <div className="px-4 py-3 border-b border-border">
              <p className="text-sm font-medium text-foreground">Ver por tenant</p>
              <p className="text-xs text-muted-foreground mt-0.5">
                Selecione um tenant para ver e editar seus overrides
              </p>
            </div>

            {/* Search */}
            <div className="p-3 border-b border-border">
              <div className="relative">
                <Search className="absolute left-2.5 top-1/2 -translate-y-1/2 h-3.5 w-3.5 text-muted-foreground pointer-events-none" />
                <input
                  placeholder="Buscar tenant..."
                  value={searchTenant}
                  onChange={e => setSearchTenant(e.target.value)}
                  className="w-full h-8 pl-8 pr-3 rounded-md border border-border bg-muted/30 text-sm focus:outline-none focus:ring-2 focus:ring-primary/20"
                />
              </div>
            </div>

            {/* Tenant list */}
            <div className="max-h-64 overflow-y-auto divide-y divide-border">
              {filteredTenants.length === 0 ? (
                <p className="p-4 text-xs text-muted-foreground text-center">Nenhum tenant encontrado.</p>
              ) : (
                filteredTenants.slice(0, 30).map(t => (
                  <button
                    key={t.id}
                    onClick={() => setSelectedTenantId(id => id === t.id ? null : t.id)}
                    className={cn(
                      "w-full flex items-center gap-2 px-3 py-2.5 text-left transition-colors hover:bg-muted/50",
                      selectedTenantId === t.id && "bg-primary/5 border-l-2 border-primary"
                    )}
                  >
                    <div className="min-w-0 flex-1">
                      <p className="text-sm font-medium text-foreground truncate">
                        {t.tradeName ?? t.companyName}
                      </p>
                      <p className="text-[10px] text-muted-foreground truncate">{t.email}</p>
                    </div>
                    <span className={cn(
                      "shrink-0 px-1.5 py-0.5 rounded text-[9px] font-medium",
                      t.status === "Active" ? "bg-green-500/10 text-green-600" : "bg-muted text-muted-foreground"
                    )}>
                      {t.status === "Active" ? "Ativo" : t.status}
                    </span>
                  </button>
                ))
              )}
            </div>
          </div>
        </div>

      </div>

      {/* ── Tenant overrides expanded ─────────────────────────────────────────── */}
      {selectedTenantId && (
        <div className="space-y-3">
          <div className="flex items-center gap-2">
            <div className="h-px flex-1 bg-border" />
            <p className="text-xs text-muted-foreground px-2">
              Flags de{" "}
              <span className="font-medium text-foreground">
                {tenants.find(t => t.id === selectedTenantId)?.tradeName ??
                 tenants.find(t => t.id === selectedTenantId)?.companyName}
              </span>
            </p>
            <div className="h-px flex-1 bg-border" />
          </div>
          <p className="text-xs text-muted-foreground">
            Toggle altera o valor do tenant. <span className="text-foreground">OVERRIDE</span> = diferente do padrão global.
            Clique em <RotateCcw className="inline h-3 w-3" /> para restaurar padrão.
          </p>
          <TenantFilterPanel tenantId={selectedTenantId} />
        </div>
      )}

    </div>
  );
}
