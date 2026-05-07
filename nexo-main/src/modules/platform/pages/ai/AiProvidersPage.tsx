import { useState } from "react";
import { Key, RefreshCw, Check, X, Star, ShieldAlert, AlertCircle, Eye, EyeOff, Trash2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogHeader,
  DialogTitle,
  DialogFooter,
} from "@/components/ui/dialog";
import { useAiProviders, useUpdateAiProvider, useRotateAiProviderKey } from "../../hooks/useAiOperations";
import type { AiProviderConfig, AnalyzerProvider } from "../../types/aiOperations";

const PROVIDER_BADGE: Record<AnalyzerProvider, string> = {
  RuleBased: "bg-blue-500/10 text-blue-600 border-blue-500/20",
  Claude:    "bg-violet-500/10 text-violet-600 border-violet-500/20",
  OpenAI:    "bg-green-500/10 text-green-600 border-green-500/20",
};

function microToUsd(micros: number) {
  if (micros === 0) return "Grátis";
  return `$${(micros / 1_000_000).toFixed(6)}/tok`;
}

// ── API Key Dialog ─────────────────────────────────────────────────────────────

function ApiKeyDialog({
  provider,
  open,
  onClose,
}: {
  provider: AiProviderConfig;
  open: boolean;
  onClose: () => void;
}) {
  const rotateMut = useRotateAiProviderKey();
  const [apiKey,   setApiKey]   = useState("");
  const [visible,  setVisible]  = useState(false);
  const [saved,    setSaved]    = useState(false);

  const handleSave = async () => {
    if (!apiKey.trim()) return;
    await rotateMut.mutateAsync({ id: provider.id, apiKey: apiKey.trim() });
    setSaved(true);
    setTimeout(() => { setSaved(false); setApiKey(""); onClose(); }, 1500);
  };

  const handleClear = async () => {
    if (!confirm(`Remover a API Key de ${provider.name}? O provider ficará inativo.`)) return;
    await rotateMut.mutateAsync({ id: provider.id, apiKey: undefined });
    onClose();
  };

  const handleClose = () => { setApiKey(""); setVisible(false); setSaved(false); onClose(); };

  return (
    <Dialog open={open} onOpenChange={handleClose}>
      <DialogContent className="max-w-md">
        <DialogHeader>
          <DialogTitle className="flex items-center gap-2">
            <Key className="h-4 w-4 text-muted-foreground" />
            API Key — {provider.name}
          </DialogTitle>
        </DialogHeader>

        <div className="space-y-4 py-2">
          {provider.apiKeyLastFour && (
            <div className="flex items-center justify-between px-3 py-2 bg-muted/40 rounded-lg text-sm">
              <div className="flex items-center gap-2">
                <span className="text-muted-foreground font-mono">****·****·****·{provider.apiKeyLastFour}</span>
                <span className="text-[10px] bg-green-500/10 text-green-600 px-1.5 py-0.5 rounded">Ativa</span>
              </div>
              <button
                onClick={handleClear}
                disabled={rotateMut.isPending}
                className="text-muted-foreground hover:text-destructive transition-colors"
                title="Remover chave"
              >
                <Trash2 className="h-3.5 w-3.5" />
              </button>
            </div>
          )}

          <div className="space-y-1.5">
            <label className="text-xs font-medium text-foreground">
              {provider.apiKeyLastFour ? "Nova chave (substitui a atual)" : "Chave de API"}
            </label>
            <div className="relative">
              <input
                type={visible ? "text" : "password"}
                value={apiKey}
                onChange={e => setApiKey(e.target.value)}
                placeholder="sk-..."
                className="w-full h-9 px-3 pr-9 rounded-md border border-input bg-background text-sm font-mono focus:outline-none focus:ring-2 focus:ring-ring"
                onKeyDown={e => e.key === "Enter" && handleSave()}
                autoFocus
              />
              <button
                type="button"
                onClick={() => setVisible(v => !v)}
                className="absolute right-2.5 top-2 text-muted-foreground hover:text-foreground"
              >
                {visible ? <EyeOff className="h-4 w-4" /> : <Eye className="h-4 w-4" />}
              </button>
            </div>
            <p className="text-[11px] text-muted-foreground">
              A chave é criptografada (AES-256) antes de ser armazenada. Apenas os últimos 4 caracteres ficam visíveis.
            </p>
          </div>
        </div>

        <DialogFooter className="gap-2">
          <Button variant="outline" size="sm" onClick={handleClose} disabled={rotateMut.isPending}>
            Cancelar
          </Button>
          <Button
            size="sm"
            onClick={handleSave}
            disabled={!apiKey.trim() || rotateMut.isPending}
            className="min-w-[80px]"
          >
            {saved ? (
              <><Check className="h-3.5 w-3.5 mr-1.5" />Salvo!</>
            ) : rotateMut.isPending ? (
              <RefreshCw className="h-3.5 w-3.5 animate-spin" />
            ) : (
              <><Key className="h-3.5 w-3.5 mr-1.5" />Salvar</>
            )}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

// ── Provider Row ───────────────────────────────────────────────────────────────

function ProviderRow({ p }: { p: AiProviderConfig }) {
  const updateMut     = useUpdateAiProvider();
  const [keyDialog,   setKeyDialog]   = useState(false);

  const toggle = () => updateMut.mutate({ id: p.id, patch: { isEnabled: !p.isEnabled } });

  return (
    <>
      <div className="flex items-start gap-4 px-5 py-4 border-b border-border last:border-0">

        {/* Provider name + badge */}
        <div className="w-48 shrink-0">
          <div className="flex items-center gap-2 mb-1">
            <span className={`text-[10px] font-medium px-1.5 py-0.5 rounded border ${PROVIDER_BADGE[p.provider]}`}>
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
          <p className="text-[11px] text-muted-foreground mb-1.5">Status</p>
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
          <p className="text-[11px] text-muted-foreground mb-1.5">API Key</p>
          {p.provider === "RuleBased" ? (
            <span className="text-xs text-muted-foreground italic">Motor local — sem chave</span>
          ) : p.apiKeyLastFour ? (
            <div className="flex items-center gap-2">
              <span className="text-sm font-mono text-foreground">
                ****·****·****·{p.apiKeyLastFour}
              </span>
              <button
                onClick={() => setKeyDialog(true)}
                title="Alterar API Key"
                className="text-muted-foreground hover:text-foreground transition-colors"
              >
                <Key className="h-3.5 w-3.5" />
              </button>
            </div>
          ) : (
            <Button
              variant="outline"
              size="sm"
              className="h-7 text-xs px-2.5 gap-1.5"
              onClick={() => setKeyDialog(true)}
            >
              <Key className="h-3 w-3" />
              Configurar chave
            </Button>
          )}
        </div>

        {/* Cost */}
        <div className="w-36 shrink-0 text-right">
          <p className="text-[11px] text-muted-foreground mb-1.5">Custo/token</p>
          <p className="text-xs text-foreground">↑ {microToUsd(p.costPerInputTokenMicros)}</p>
          <p className="text-xs text-foreground">↓ {microToUsd(p.costPerOutputTokenMicros)}</p>
        </div>

        {/* Monthly limit */}
        <div className="w-28 shrink-0 text-right">
          <p className="text-[11px] text-muted-foreground mb-1.5">Limite/mês</p>
          <p className="text-xs text-foreground">
            {p.monthlyTokenLimit
              ? `${(p.monthlyTokenLimit / 1_000_000).toFixed(1)}M tok`
              : "Sem limite"}
          </p>
        </div>
      </div>

      {p.provider !== "RuleBased" && (
        <ApiKeyDialog
          provider={p}
          open={keyDialog}
          onClose={() => setKeyDialog(false)}
        />
      )}
    </>
  );
}

// ── Page ───────────────────────────────────────────────────────────────────────

export default function AiProvidersPage() {
  const { data: providers, isLoading, isError, error } = useAiProviders();

  return (
    <div className="p-6 space-y-5 max-w-5xl">

      {/* Header */}
      <div>
        <h1 className="text-xl font-semibold text-foreground">Gestão de Providers</h1>
        <p className="text-sm text-muted-foreground mt-0.5">
          Configure analyzers, API Keys, fallback chain e limites de uso.
        </p>
      </div>

      {/* Security notice */}
      <div className="flex items-start gap-3 bg-amber-50 border border-amber-200 rounded-lg px-4 py-3">
        <ShieldAlert className="h-4 w-4 text-amber-600 shrink-0 mt-0.5" />
        <div>
          <p className="text-xs font-medium text-amber-800">
            API Keys são armazenadas com criptografia AES-256.
          </p>
          <p className="text-xs text-amber-700 mt-0.5">
            Apenas os últimos 4 caracteres são exibidos. Todas as alterações são registradas no audit trail.
          </p>
        </div>
      </div>

      {/* Providers list */}
      <div className="bg-card border border-border rounded-lg overflow-hidden">
        <div className="px-5 py-3 border-b border-border bg-muted/30 flex items-center justify-between">
          <h2 className="text-sm font-medium text-foreground">Providers configurados</h2>
          <span className="text-xs text-muted-foreground">Prioridade: RuleBased → Claude → OpenAI</span>
        </div>

        {isLoading && (
          <div className="p-8 text-center text-sm text-muted-foreground">
            Carregando providers...
          </div>
        )}

        {isError && (
          <div className="flex items-center gap-3 p-6 text-sm text-destructive">
            <AlertCircle className="h-4 w-4 shrink-0" />
            <div>
              <p className="font-medium">Erro ao carregar providers</p>
              <p className="text-xs text-muted-foreground mt-0.5">
                {(error as Error)?.message ?? "Falha na comunicação com o backend."}
              </p>
            </div>
          </div>
        )}

        {!isLoading && !isError && providers?.length === 0 && (
          <div className="p-8 text-center text-sm text-muted-foreground">
            Nenhum provider configurado.
          </div>
        )}

        {!isLoading && !isError && providers && providers.length > 0 && (
          <div>
            {providers.map(p => <ProviderRow key={p.id} p={p} />)}
          </div>
        )}
      </div>

      {/* Fallback chain visualization */}
      {providers && providers.length > 0 && (
        <div className="bg-card border border-border rounded-lg p-4">
          <h3 className="text-sm font-medium text-foreground mb-3">Fallback Chain (modo automático)</h3>
          <div className="flex items-center gap-2 flex-wrap">
            {providers
              .filter(p => p.isEnabled)
              .sort((a, b) => a.priority - b.priority)
              .map((p, i, arr) => (
                <div key={p.id} className="flex items-center gap-2">
                  <div className={`flex items-center gap-1.5 px-3 py-1.5 rounded-full text-xs font-medium border ${PROVIDER_BADGE[p.provider]}`}>
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
                Nenhum provider ativo — apenas análise rule-based disponível.
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
