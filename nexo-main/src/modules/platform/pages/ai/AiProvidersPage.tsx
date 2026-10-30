import { useState, type FormEvent } from "react";
import { toast } from "sonner";
import { Key, RefreshCw, Star, ShieldCheck, ShieldAlert, Trash2, PlugZap, Ban, Save } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Switch } from "@/components/ui/switch";
import { Checkbox } from "@/components/ui/checkbox";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogHeader,
  DialogTitle,
  DialogFooter,
} from "@/components/ui/dialog";
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
} from "@/components/ui/alert-dialog";
import {
  useAiProviders,
  useUpdateAiProvider,
  useSetAiProviderKey,
  useClearAiProviderKey,
  useTestAiProvider,
} from "../../hooks/useAiOperations";
import type { AiProviderConfig, AiProviderPatch, AiProviderTestResult } from "../../types/aiOperations";
import { ApiErrorPanel, ProviderStatusBadge, errorText, formatDateTime, formatUsd, reasonLabel } from "./aiShared";

const KEY_MIN = 20;
const KEY_MAX = 400;

/** Same rule as the backend (20–400 characters, no spaces): only to explain before sending. */
function keyProblem(key: string): string | null {
  if (!key) return null;
  if (/\s/.test(key)) return "A chave não pode ter espaços.";
  if (key.length < KEY_MIN) return `A chave precisa ter pelo menos ${KEY_MIN} caracteres.`;
  if (key.length > KEY_MAX) return `A chave pode ter no máximo ${KEY_MAX} caracteres.`;
  return null;
}

// ── API key dialog ─────────────────────────────────────────────────────────────

function ApiKeyDialog({
  provider,
  open,
  onClose,
}: Readonly<{ provider: AiProviderConfig; open: boolean; onClose: () => void }>) {
  const setKey = useSetAiProviderKey();
  // never pre-filled, never read back from the server: the key exists only while it is typed
  const [apiKey, setApiKey] = useState("");

  const close = () => { setApiKey(""); onClose(); };
  const trimmed = apiKey.trim();
  const problem = keyProblem(trimmed);

  const save = (e: FormEvent) => {
    e.preventDefault();
    if (!trimmed || problem) return;
    setKey.mutate({ id: provider.id, apiKey: trimmed }, {
      onSuccess: (updated) => {
        toast.success(`Chave de ${provider.name} salva (****${updated.apiKeyLastFour ?? ""}).`);
        close();
      },
      onError: (err) => toast.error(errorText(err)),
    });
  };

  return (
    <Dialog open={open} onOpenChange={(o) => { if (!o) close(); }}>
      <DialogContent className="max-w-md">
        <form onSubmit={save}>
          <DialogHeader>
            <DialogTitle className="flex items-center gap-2">
              <Key className="h-4 w-4 text-muted-foreground" />
              {provider.hasApiKey ? "Trocar chave" : "Cadastrar chave"} — {provider.name}
            </DialogTitle>
            <DialogDescription>
              A chave é criptografada (AES-256-GCM) com uma chave mestra que fica fora do banco. Depois de salva,
              ninguém consegue vê-la: o Orken mostra só os 4 últimos caracteres.
            </DialogDescription>
          </DialogHeader>

          <div className="space-y-1.5 py-4">
            <label htmlFor={`${provider.id}-api-key`} className="text-xs font-medium text-foreground">
              {provider.hasApiKey ? `Nova chave (substitui ****${provider.apiKeyLastFour ?? ""})` : "Chave de API"}
            </label>
            <Input
              id={`${provider.id}-api-key`}
              type="password"
              autoComplete="off"
              spellCheck={false}
              value={apiKey}
              onChange={e => setApiKey(e.target.value)}
              className="font-mono"
              autoFocus
            />
            {problem && <p className="text-[11px] text-destructive">{problem}</p>}
          </div>

          <DialogFooter className="gap-2">
            <Button type="button" variant="outline" size="sm" onClick={close} disabled={setKey.isPending}>
              Cancelar
            </Button>
            <Button type="submit" size="sm" disabled={!trimmed || !!problem || setKey.isPending} className="min-w-[96px]">
              {setKey.isPending
                ? <RefreshCw className="h-3.5 w-3.5 animate-spin" />
                : <><Key className="h-3.5 w-3.5 mr-1.5" />Salvar chave</>}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

// ── Settings form (model, priority, prices, monthly limit) ───────────────────

interface SettingsDraft {
  modelId:     string;
  priority:    string;
  inputPrice:  string;
  outputPrice: string;
  limit:       string;
  unlimited:   boolean;
}

function draftOf(p: AiProviderConfig): SettingsDraft {
  return {
    modelId:     p.modelId ?? "",
    priority:    String(p.priority),
    inputPrice:  String(p.inputUsdPerMillionTokens),
    outputPrice: String(p.outputUsdPerMillionTokens),
    limit:       p.monthlyTokenLimit == null ? "" : String(p.monthlyTokenLimit),
    unlimited:   p.monthlyTokenLimit == null,
  };
}

/** Only what changed, validated as the backend will; a string is the problem to show. */
function buildPatch(p: AiProviderConfig, d: SettingsDraft): AiProviderPatch | string {
  const patch: AiProviderPatch = {};
  const model = d.modelId.trim();
  if (p.requiresKey && model !== (p.modelId ?? "")) patch.modelId = model;

  const priority = Number(d.priority);
  if (!Number.isInteger(priority) || priority < 1 || priority > 100) return "A prioridade deve ser um número inteiro de 1 a 100.";
  if (priority !== p.priority) patch.priority = priority;

  const input = Number(d.inputPrice.replace(",", "."));
  const output = Number(d.outputPrice.replace(",", "."));
  if (d.inputPrice.trim() === "" || !Number.isFinite(input) || input < 0) return "O preço de entrada deve ser um número maior ou igual a zero.";
  if (d.outputPrice.trim() === "" || !Number.isFinite(output) || output < 0) return "O preço de saída deve ser um número maior ou igual a zero.";
  if (input !== p.inputUsdPerMillionTokens) patch.inputUsdPerMillionTokens = input;
  if (output !== p.outputUsdPerMillionTokens) patch.outputUsdPerMillionTokens = output;

  if (d.unlimited) {
    if (p.monthlyTokenLimit != null) patch.clearMonthlyTokenLimit = true;
  } else {
    const limit = Number(d.limit);
    if (!Number.isInteger(limit) || limit < 1) return "O limite mensal deve ser um número inteiro de tokens maior que zero — ou marque “sem limite”.";
    if (limit !== p.monthlyTokenLimit) patch.monthlyTokenLimit = limit;
  }
  return patch;
}

function SettingsForm({ p, locked }: Readonly<{ p: AiProviderConfig; locked: boolean }>) {
  const update = useUpdateAiProvider();
  const [draft, setDraft] = useState<SettingsDraft>(() => draftOf(p));
  const result = buildPatch(p, draft);
  const problem = typeof result === "string" ? result : null;
  const dirty = typeof result !== "string" && Object.keys(result).length > 0;
  const set = (field: keyof SettingsDraft) => (e: React.ChangeEvent<HTMLInputElement>) =>
    setDraft(d => ({ ...d, [field]: e.target.value }));

  const submit = (e: FormEvent) => {
    e.preventDefault();
    if (typeof result === "string" || !dirty) return;
    update.mutate({ id: p.id, patch: result }, {
      onSuccess: () => toast.success(`${p.name}: configuração salva.`),
      onError:   (err) => toast.error(errorText(err)),
    });
  };

  const field = "space-y-1";
  const label = "text-[11px] text-muted-foreground block";
  return (
    <form onSubmit={submit} className="space-y-3">
      <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-3">
        {p.requiresKey && (
          <div className={`${field} sm:col-span-2`}>
            <label htmlFor={`${p.id}-model`} className={label}>Modelo</label>
            <Input id={`${p.id}-model`} value={draft.modelId} onChange={set("modelId")} disabled={locked}
              placeholder="ex.: claude-sonnet-4-5" className="h-8 font-mono text-xs" />
          </div>
        )}
        <div className={field}>
          <label htmlFor={`${p.id}-priority`} className={label}>Prioridade (1 = primeiro)</label>
          <Input id={`${p.id}-priority`} type="number" min={1} max={100} step={1} value={draft.priority}
            onChange={set("priority")} disabled={locked} className="h-8 text-xs" />
        </div>
        <div className={field}>
          <label htmlFor={`${p.id}-input-price`} className={label}>Entrada (US$ / 1M tokens)</label>
          <Input id={`${p.id}-input-price`} type="number" min={0} step="any" value={draft.inputPrice}
            onChange={set("inputPrice")} disabled={locked} className="h-8 text-xs" />
        </div>
        <div className={field}>
          <label htmlFor={`${p.id}-output-price`} className={label}>Saída (US$ / 1M tokens)</label>
          <Input id={`${p.id}-output-price`} type="number" min={0} step="any" value={draft.outputPrice}
            onChange={set("outputPrice")} disabled={locked} className="h-8 text-xs" />
        </div>
        <div className={field}>
          <label htmlFor={`${p.id}-limit`} className={label}>Limite mensal (tokens)</label>
          <Input id={`${p.id}-limit`} type="number" min={1} step={1} value={draft.unlimited ? "" : draft.limit}
            placeholder={draft.unlimited ? "Sem limite" : ""} onChange={set("limit")}
            disabled={locked || draft.unlimited} className="h-8 text-xs" />
          <label className="flex items-center gap-1.5 text-[11px] text-muted-foreground pt-0.5">
            <Checkbox
              checked={draft.unlimited}
              disabled={locked}
              onCheckedChange={(v) => setDraft(d => ({ ...d, unlimited: v === true }))}
              aria-label={`Sem limite mensal para ${p.name}`}
            />
            Sem limite
          </label>
        </div>
      </div>
      <div className="flex flex-wrap items-center gap-3">
        <Button type="submit" size="sm" variant="outline" className="h-8 text-xs gap-1.5"
          disabled={locked || !dirty || update.isPending}>
          {update.isPending ? <RefreshCw className="h-3.5 w-3.5 animate-spin" /> : <Save className="h-3.5 w-3.5" />}
          Salvar alterações
        </Button>
        {dirty && !problem && (
          <Button type="button" size="sm" variant="ghost" className="h-8 text-xs" onClick={() => setDraft(draftOf(p))}>
            Descartar
          </Button>
        )}
        {problem && !locked && <p className="text-[11px] text-destructive">{problem}</p>}
        {p.provider === "RuleBased" && (
          <p className="text-[11px] text-muted-foreground">Motor local: roda no próprio Orken e não tem custo.</p>
        )}
      </div>
    </form>
  );
}

// ── Provider card ──────────────────────────────────────────────────────────────

function testBlockedReason(p: AiProviderConfig): string | null {
  if (!p.implemented) return "Não disponível nesta versão do Orken.";
  if (p.requiresKey && !p.hasApiKey) return "Cadastre uma chave antes de testar.";
  if (p.requiresKey && !p.modelId) return "Defina o modelo antes de testar.";
  return null;
}

function TestResultView({ result }: Readonly<{ result: AiProviderTestResult }>) {
  const ok = result.status === "ok";
  return (
    <div role="status" className={`rounded-md border px-3 py-2 text-xs space-y-0.5 ${
      ok ? "border-green-500/30 bg-green-500/5" : "border-destructive/30 bg-destructive/5"}`}>
      <p className={`font-medium ${ok ? "text-green-700" : "text-destructive"}`}>
        Teste de conexão: {ok ? "OK" : reasonLabel(result.status)} <span className="font-mono font-normal">({result.status})</span>
      </p>
      <p className="text-foreground">{result.message}</p>
      <p className="text-muted-foreground">
        Modelo: <span className="font-mono">{result.modelId ?? "—"}</span> · Latência: {result.latencyMs} ms · {formatDateTime(result.testedAt)}
      </p>
    </div>
  );
}

function ProviderCard({ p, secretStorageAvailable }: Readonly<{ p: AiProviderConfig; secretStorageAvailable: boolean }>) {
  const update = useUpdateAiProvider();
  const clearKey = useClearAiProviderKey();
  const test = useTestAiProvider();
  const [keyDialog, setKeyDialog] = useState(false);
  const [confirmRemove, setConfirmRemove] = useState(false);
  const [testResult, setTestResult] = useState<AiProviderTestResult | null>(null);

  const locked = !p.implemented;
  const blockedTest = testBlockedReason(p);

  const patch = (body: AiProviderPatch, done: string) =>
    update.mutate({ id: p.id, patch: body }, {
      onSuccess: () => toast.success(done),
      onError:   (err) => toast.error(errorText(err)),
    });

  const runTest = () => {
    setTestResult(null);
    test.mutate({ id: p.id }, {
      onSuccess: (r) => {
        setTestResult(r);
        if (r.status === "ok") toast.success(`${p.name}: conexão OK.`);
        else toast.error(`${p.name}: ${r.message}`);
      },
      onError: (err) => toast.error(errorText(err)),
    });
  };

  const removeKey = () =>
    clearKey.mutate({ id: p.id }, {
      onSuccess: () => { toast.success(`Chave de ${p.name} removida.`); setConfirmRemove(false); },
      onError:   (err) => toast.error(errorText(err)),
    });

  // re-seed the form whenever the server answers new values
  const formKey = [p.modelId, p.priority, p.inputUsdPerMillionTokens, p.outputUsdPerMillionTokens, p.monthlyTokenLimit].join("|");

  return (
    <section aria-label={`Provedor ${p.name}`} className="bg-card border border-border rounded-lg p-4 sm:p-5 space-y-4">
      {/* Identity + status */}
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="min-w-0">
          <div className="flex flex-wrap items-center gap-2">
            <h2 className="text-sm font-semibold text-foreground">{p.name}</h2>
            <span className="text-[10px] font-mono text-muted-foreground border border-border rounded px-1.5 py-0.5">{p.provider}</span>
            <ProviderStatusBadge status={p.status} />
            {p.isDefault && (
              <span className="flex items-center gap-0.5 text-[11px] text-amber-700">
                <Star className="h-3 w-3 fill-amber-400 text-amber-400" /> Padrão
              </span>
            )}
          </div>
          <p className="text-xs text-muted-foreground mt-1">{p.statusMessage}</p>
          {p.modelId && <p className="text-xs text-foreground mt-0.5">Modelo: <span className="font-mono">{p.modelId}</span></p>}
        </div>

        <div className="flex flex-wrap items-center gap-3">
          <label className="flex items-center gap-2 text-xs text-foreground">
            <Switch
              checked={p.isEnabled}
              disabled={locked || update.isPending}
              onCheckedChange={(v) => patch({ isEnabled: v }, `${p.name} ${v ? "ativado" : "desativado"}.`)}
              aria-label={`Ativar ${p.name}`}
            />
            {p.isEnabled ? "Ativado" : "Desativado"}
          </label>
          <Button
            size="sm" variant="outline" className="h-8 text-xs gap-1.5"
            disabled={locked || p.isDefault || update.isPending}
            onClick={() => patch({ isDefault: true }, `${p.name} agora é o provedor padrão.`)}
          >
            <Star className="h-3.5 w-3.5" />
            {p.isDefault ? "É o padrão" : "Definir como padrão"}
          </Button>
        </div>
      </div>

      {locked && (
        <div className="flex items-center gap-2 rounded-md border border-border bg-muted/40 px-3 py-2 text-xs text-muted-foreground">
          <Ban className="h-3.5 w-3.5 shrink-0" />
          Não disponível nesta versão do Orken — nada aqui pode ser alterado nem usado.
        </div>
      )}

      {/* API key */}
      {p.requiresKey ? (
        <div className="space-y-1.5">
          <p className="text-[11px] text-muted-foreground">Chave de API</p>
          <div className="flex flex-wrap items-center gap-2">
            {p.hasApiKey ? (
              <span className="text-sm font-mono text-foreground" data-testid={`key-${p.provider}`}>****{p.apiKeyLastFour}</span>
            ) : (
              <span className="text-xs text-muted-foreground italic">Nenhuma chave cadastrada</span>
            )}
            {p.hasApiKey && p.apiKeyUpdatedAt && (
              <span className="text-[11px] text-muted-foreground">atualizada em {formatDateTime(p.apiKeyUpdatedAt)}</span>
            )}
            <Button
              size="sm" variant="outline" className="h-8 text-xs gap-1.5"
              disabled={locked || !secretStorageAvailable}
              onClick={() => setKeyDialog(true)}
            >
              <Key className="h-3.5 w-3.5" />
              {p.hasApiKey ? "Trocar chave" : "Cadastrar chave"}
            </Button>
            {p.hasApiKey && (
              <Button
                size="sm" variant="ghost" className="h-8 text-xs gap-1.5 text-destructive hover:text-destructive"
                disabled={locked || clearKey.isPending}
                onClick={() => setConfirmRemove(true)}
              >
                <Trash2 className="h-3.5 w-3.5" /> Remover chave
              </Button>
            )}
          </div>
          {!secretStorageAvailable && !locked && (
            <p className="text-[11px] text-destructive">Esta implantação não consegue guardar chaves (Secrets:MasterKey ausente).</p>
          )}
        </div>
      ) : (
        <p className="text-xs text-muted-foreground">Não usa chave de API.</p>
      )}

      {/* Model, priority, prices, limit */}
      <SettingsForm key={formKey} p={p} locked={locked} />

      <p className="text-[11px] text-muted-foreground">
        Preço atual: entrada {formatUsd(p.inputUsdPerMillionTokens)} · saída {formatUsd(p.outputUsdPerMillionTokens)} por 1M tokens ·
        limite mensal {p.monthlyTokenLimit == null ? "sem limite" : `${p.monthlyTokenLimit.toLocaleString("pt-BR")} tokens`}
      </p>

      {/* Connection test */}
      <div className="space-y-2 border-t border-border pt-3">
        <div className="flex flex-wrap items-center gap-3">
          <Button
            size="sm" variant="secondary" className="h-8 text-xs gap-1.5"
            disabled={!!blockedTest || test.isPending}
            onClick={runTest}
          >
            {test.isPending ? <RefreshCw className="h-3.5 w-3.5 animate-spin" /> : <PlugZap className="h-3.5 w-3.5" />}
            Testar conexão
          </Button>
          {blockedTest
            ? <p className="text-[11px] text-muted-foreground">{blockedTest}</p>
            : p.requiresKey && <p className="text-[11px] text-muted-foreground">Faz uma chamada mínima (paga) ao provedor, só quando clicado.</p>}
        </div>
        {testResult && <TestResultView result={testResult} />}
        {!testResult && p.lastTest && (
          <p className="text-[11px] text-muted-foreground">
            Último teste: <span className="font-medium">{reasonLabel(p.lastTest.status)}</span> — {p.lastTest.message} ({formatDateTime(p.lastTest.at)})
          </p>
        )}
      </div>

      {p.requiresKey && (
        <ApiKeyDialog provider={p} open={keyDialog} onClose={() => setKeyDialog(false)} />
      )}

      <AlertDialog open={confirmRemove} onOpenChange={setConfirmRemove}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Remover a chave de {p.name}?</AlertDialogTitle>
            <AlertDialogDescription>
              A chave ****{p.apiKeyLastFour} é apagada do Orken. {p.name} fica sem chave e deixa de ser usado até receber outra.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={clearKey.isPending}>Cancelar</AlertDialogCancel>
            <AlertDialogAction
              disabled={clearKey.isPending}
              onClick={(e) => { e.preventDefault(); removeKey(); }}
              className="bg-destructive text-destructive-foreground hover:bg-destructive/90"
            >
              Remover chave
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </section>
  );
}

// ── Page ───────────────────────────────────────────────────────────────────────

export default function AiProvidersPage() {
  const { data, isLoading, isError, error } = useAiProviders();
  const providers = data?.providers ?? [];

  return (
    <div className="p-4 sm:p-6 space-y-5 max-w-5xl">
      <div>
        <h1 className="font-display text-[20px] font-bold text-foreground tracking-tight">Provedores de IA</h1>
        <p className="text-sm text-muted-foreground mt-0.5">
          Status real de cada provedor, chaves, modelo, prioridade, preços e limites.
        </p>
      </div>

      {isLoading && (
        <div className="p-6 text-center text-sm text-muted-foreground">Carregando provedores...</div>
      )}

      {isError && <ApiErrorPanel error={error} title="Não foi possível carregar os provedores" />}

      {data && !data.secretStorageAvailable && (
        <div role="alert" className="flex items-start gap-3 rounded-lg border border-destructive/30 bg-destructive/5 px-4 py-3">
          <ShieldAlert className="h-4 w-4 text-destructive shrink-0 mt-0.5" />
          <div>
            <p className="text-sm font-medium text-destructive">Esta implantação não consegue guardar chaves de API</p>
            <p className="text-xs text-foreground mt-0.5">
              A chave mestra (Secrets:MasterKey) está ausente no servidor. Nenhuma chave é salva até ela ser configurada,
              e provedores que dependem de chave ficam indisponíveis.
            </p>
          </div>
        </div>
      )}

      {data && (
        <div className="bg-card border border-border rounded-lg p-4 space-y-2">
          <h2 className="text-sm font-medium text-foreground">Modo automático</h2>
          {data.automaticOrder.length > 0 ? (
            <p className="text-sm text-foreground">
              Modo automático usa: <span className="font-medium">{data.automaticOrder.join(" → ")}</span>
            </p>
          ) : (
            <p className="text-sm text-destructive">Modo automático: nenhum provedor disponível.</p>
          )}
          <p className="text-xs text-muted-foreground">
            O provedor padrão roda primeiro; depois os demais disponíveis, por prioridade. Se um falhar, o próximo assume.
            O RuleBased roda localmente e não tem custo.
          </p>
        </div>
      )}

      {data && (
        <div className="flex items-start gap-3 rounded-lg border border-border bg-muted/30 px-4 py-3">
          <ShieldCheck className="h-4 w-4 text-muted-foreground shrink-0 mt-0.5" />
          <p className="text-xs text-muted-foreground">
            A chave é criptografada (AES-256-GCM) com uma chave mestra que fica fora do banco. Depois de salva, ninguém
            consegue vê-la: o Orken mostra só os 4 últimos caracteres. Toda alteração fica registrada na auditoria.
          </p>
        </div>
      )}

      {data && providers.length === 0 && (
        <div className="p-6 text-center text-sm text-muted-foreground">Nenhum provedor configurado.</div>
      )}

      {providers.map(p => (
        <ProviderCard key={p.id} p={p} secretStorageAvailable={data?.secretStorageAvailable ?? false} />
      ))}
    </div>
  );
}
