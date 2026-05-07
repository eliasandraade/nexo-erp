import { useState } from "react";
import { Key, RefreshCw, Check, X, Star, StarOff, ShieldAlert } from "lucide-react";
import { Button } from "@/components/ui/button";
import { useAiProviders, useUpdateAiProvider, useRotateAiProviderKey } from "../../hooks/useAiOperations";
import type { AiProviderConfig, AnalyzerProvider } from "../../types/aiOperations";

const PROVIDER_BADGE: Record<AnalyzerProvider, string> = {
  RuleBased: "bg-blue-500/10 text-blue-600",
  Claude:    "bg-violet-500/10 text-violet-600",
  OpenAI:    "bg-green-500/10 text-green-600",
};

function microToUsd(micros: number) {
  if (micros === 0) return "Grátis";
  return `$${(micros / 1_000_000).toFixed(6)}/tok`;
}

function ProviderRow({ p }: { p: AiProviderConfig }) {
  const updateMut  = useUpdateAiProvider();
  const rotateMut  = useRotateAiProviderKey();
  const [rotating, setRotating] = useState(false);
  const [newKey,   setNewKey]   = useState<string | null>(null);

  const toggle = () => updateMut.mutate({ id: p.id, patch: { isEnabled: !p.isEnabled } });

  const handleRotate = async () => {
    if (!confirm("Rotacionar a API Key invalida a chave atual imediatamente. Confirmar?")) return;
    setRotating(true);
    const res = await rotateMut.mutateAsync(p.id);
    setNewKey(res.lastFour);
    setRotating(false);
    setTimeout(() => setNewKey(null), 10_000); // hide after 10s
  };

  return (
    <div className="flex items-start gap-4 px-5 py-4 border-b border-border last:border-0">
      {/* Provider name + badge */}
      <div className="w-44 shrink-0">
        <div className="flex items-center gap-2 mb-1">
          <span className={`text-[10px] font-medium px-1.5 py-0.5 rounded ${PROVIDER_BADGE[p.provider]}`}>
            {p.provider}
          </span>
          {p.isDefault && (
            <span className="flex items-center gap-0.5 text-[10px] text-amber-600">
              <Star className="h-3 w-3 fill-amber-400 text-amber-400" />
              Padrão
            </span>
          )}
        </div>
        <p className="text-sm font-medium text-foreground">{p.name}</p>
        {p.modelId && (
          <p className="text-xs text-muted-foreground font-mono mt-0.5">{p.modelId}</p>
        )}
      </div>

      {/* Status toggle */}
      <div className="w-28 shrink-0">
        <p className="text-xs text-muted-foreground mb-1.5">Status</p>
        <button
          onClick={toggle}
          disabled={updateMut.isPending}
          className={`flex items-center gap-1.5 px-2.5 py-1 rounded-full text-xs font-medium transition-colors ${
            p.isEnabled
              ? "bg-green-500/10 text-green-600 hover:bg-green-500/20"
              : "bg-muted text-muted-foreground hover:bg-muted/80"
          }`}
        >
          {p.isEnabled ? (
            <><Check className="h-3 w-3" /> Ativo</>
          ) : (
            <><X className="h-3 w-3" /> Inativo</>
          )}
        </button>
      </div>

      {/* API Key */}
      <div className="flex-1">
        <p className="text-xs text-muted-foreground mb-1.5">API Key</p>
        {p.apiKeyLastFour ? (
          <div className="flex items-center gap-2">
            <span className="text-sm font-mono text-foreground">
              ****·****·****·{newKey ?? p.apiKeyLastFour}
            </span>
            {newKey && (
              <span className="text-[10px] text-green-600 bg-green-500/10 px-1.5 py-0.5 rounded">
                Rotacionada
              </span>
            )}
            <button
              onClick={handleRotate}
              disabled={rotating || rotateMut.isPending}
              title="Rotacionar API Key"
              className="text-muted-foreground hover:text-destructive transition-colors"
            >
              <RefreshCw className={`h-3.5 w-3.5 ${rotating ? "animate-spin" : ""}`} />
            </button>
          </div>
        ) : (
          <div className="flex items-center gap-2">
            <span className="text-xs text-muted-foreground italic">Não configurada</span>
            <Button variant="outline" size="sm" className="h-6 text-xs px-2">
              <Key className="h-3 w-3 mr-1" />
              Configurar
            </Button>
          </div>
        )}
      </div>

      {/* Cost */}
      <div className="w-36 shrink-0 text-right">
        <p className="text-xs text-muted-foreground mb-1.5">Custo</p>
        <p className="text-xs text-foreground">↑ {microToUsd(p.costPerInputTokenMicros)}</p>
        <p className="text-xs text-foreground">↓ {microToUsd(p.costPerOutputTokenMicros)}</p>
      </div>

      {/* Monthly limit */}
      <div className="w-28 shrink-0 text-right">
        <p className="text-xs text-muted-foreground mb-1.5">Limite/mês</p>
        <p className="text-xs text-foreground">
          {p.monthlyTokenLimit
            ? `${(p.monthlyTokenLimit / 1_000_000).toFixed(1)}M tok`
            : "Sem limite"}
        </p>
      </div>
    </div>
  );
}

export default function AiProvidersPage() {
  const { data: providers, isLoading } = useAiProviders();

  return (
    <div className="p-6 space-y-5 max-w-5xl">

      {/* Header */}
      <div className="flex items-center justify-between">
        <div>
          <h1 className="text-xl font-semibold text-foreground">Gestão de Providers</h1>
          <p className="text-sm text-muted-foreground mt-0.5">
            Configure analyzers, API Keys, fallback chain e limites de uso.
          </p>
        </div>
      </div>

      {/* Security notice */}
      <div className="flex items-start gap-3 bg-amber-50 border border-amber-200 rounded-lg px-4 py-3">
        <ShieldAlert className="h-4 w-4 text-amber-600 shrink-0 mt-0.5" />
        <div>
          <p className="text-xs font-medium text-amber-800">
            API Keys são armazenadas com criptografia AES-256.
          </p>
          <p className="text-xs text-amber-700 mt-0.5">
            Apenas os últimos 4 caracteres são exibidos. Após rotação, a chave anterior é invalidada imediatamente.
            Todas as alterações são registradas no audit trail.
          </p>
        </div>
      </div>

      {/* Providers list */}
      <div className="bg-card border border-border rounded-lg overflow-hidden">
        <div className="px-5 py-3 border-b border-border bg-muted/30 flex items-center justify-between">
          <h2 className="text-sm font-medium text-foreground">Providers configurados</h2>
          <div className="flex items-center gap-2 text-xs text-muted-foreground">
            <span>Prioridade: RuleBased → Claude → OpenAI</span>
          </div>
        </div>

        {isLoading ? (
          <div className="p-8 text-center text-sm text-muted-foreground">Carregando...</div>
        ) : (
          <div>
            {providers?.map(p => <ProviderRow key={p.id} p={p} />)}
          </div>
        )}
      </div>

      {/* Fallback chain visualization */}
      {providers && !isLoading && (
        <div className="bg-card border border-border rounded-lg p-4">
          <h3 className="text-sm font-medium text-foreground mb-3">Fallback Chain (auto-mode)</h3>
          <div className="flex items-center gap-2 flex-wrap">
            {providers
              .filter(p => p.isEnabled)
              .sort((a, b) => a.priority - b.priority)
              .map((p, i, arr) => (
                <div key={p.id} className="flex items-center gap-2">
                  <div className={`flex items-center gap-1.5 px-3 py-1.5 rounded-full text-xs font-medium border ${PROVIDER_BADGE[p.provider]} border-current/20`}>
                    {p.isDefault && <Star className="h-3 w-3 fill-current" />}
                    {p.name}
                  </div>
                  {i < arr.length - 1 && (
                    <span className="text-muted-foreground text-sm">→</span>
                  )}
                </div>
              ))}
            {providers.filter(p => p.isEnabled).length === 0 && (
              <p className="text-xs text-muted-foreground">
                Nenhum provider ativo. Apenas análise rule-based disponível.
              </p>
            )}
          </div>
          <p className="text-xs text-muted-foreground mt-3">
            Quando um provider falha ou está desabilitado, o próximo na chain é utilizado automaticamente.
          </p>
        </div>
      )}
    </div>
  );
}
