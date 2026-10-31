import { useState, type FormEvent } from "react";
import { toast } from "sonner";
import { FileCode, CheckCircle2, Clock, Hash, ChevronDown, ChevronUp, Plus, RefreshCw, Save, ShieldAlert, Zap, CircleOff } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
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
import { ApiError } from "@/services/api-client";
import { useActivatePromptVersion, useCreatePromptVersion, usePromptTypes, usePromptVersions } from "../../hooks/useAiOperations";
import type { PromptType, PromptVersion } from "../../types/aiOperations";
import { ApiErrorPanel, formatDateTime, governedErrorText } from "./aiShared";

const CONTENT_MAX = 16_000;
const VERSION_MAX = 20;
const VERSION_CHARS = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789.-";

interface PromptTypeOption { value: PromptType; label: string; usedByRuntime: boolean; builtInHash: string | null }

/** Friendly names; a type the endpoint lists and this map does not know shows its raw key. */
const PROMPT_TYPE_LABEL: Record<string, string> = {
  extraction:                    "Extração",
  interpretation:                "Interpretação",
  memory:                        "Memória",
  "build-daily-log-copilot":     "Build · Copiloto do diário",
  "build-progress-interpreter":  "Build · Interpretação do progresso",
  "build-model-mapper":          "Build · Vínculos do modelo",
  "build-client-update":         "Build · Atualização ao cliente",
  "build-weekly-digest":         "Build · Resumo semanal",
  "build-delay-risk":            "Build · Risco de prazo",
  "build-cost-risk":             "Build · Risco de custo",
  "build-document-intelligence": "Build · Análise de documentos",
};

/** Used only while GET …/prompts/types has not answered (or failed): the platform types as they were before. */
const FALLBACK_TYPES: PromptTypeOption[] = [
  { value: "extraction",     label: "Extração",      usedByRuntime: true,  builtInHash: null },
  { value: "interpretation", label: "Interpretação", usedByRuntime: false, builtInHash: null },
  { value: "memory",         label: "Memória",       usedByRuntime: false, builtInHash: null },
];

const isBuildType = (t: string) => t.startsWith("build-");

const CONCURRENT_ACTIVATION = "Outra versão deste prompt acabou de ser ativada. A lista foi atualizada.";

/** Same rule as the backend (letters, digits, '.', '-', up to 20): only to explain before sending. */
function versionProblem(version: string): string | null {
  if (!version) return null;
  if (version.length > VERSION_MAX) return `A versão pode ter no máximo ${VERSION_MAX} caracteres.`;
  if (![...version].every(ch => VERSION_CHARS.includes(ch))) return "Use só letras, números, ponto e hífen (ex.: 1.1.0).";
  return null;
}

// ── New version dialog ─────────────────────────────────────────────────────────

function NewVersionDialog({
  type,
  open,
  onClose,
}: Readonly<{ type: PromptTypeOption; open: boolean; onClose: () => void }>) {
  const create = useCreatePromptVersion();
  const [version, setVersion] = useState("");
  const [description, setDescription] = useState("");
  const [content, setContent] = useState("");
  const [failure, setFailure] = useState<string | null>(null);

  const trimmedVersion = version.trim();
  const problem = versionProblem(trimmedVersion)
    ?? (content.length > CONTENT_MAX ? `O prompt pode ter no máximo ${CONTENT_MAX.toLocaleString("pt-BR")} caracteres.` : null);
  const ready = !!trimmedVersion && content.trim().length > 0 && !problem;

  const close = () => { setVersion(""); setDescription(""); setContent(""); setFailure(null); create.reset(); onClose(); };

  const submit = (e: FormEvent) => {
    e.preventDefault();
    if (!ready) return;
    setFailure(null);
    create.mutate({ promptType: type.value, version: trimmedVersion, content, description: description.trim() }, {
      onSuccess: (created) => {
        toast.success(`Versão ${created.version} criada (inativa). Hash ${created.hash}.`);
        close();
      },
      onError: (err) => {
        const text = governedErrorText(err);
        setFailure(text);
        toast.error(text);
      },
    });
  };

  return (
    <Dialog open={open} onOpenChange={(o) => { if (!o) close(); }}>
      <DialogContent className="max-w-2xl">
        <form onSubmit={submit}>
          <DialogHeader>
            <DialogTitle className="flex items-center gap-2">
              <FileCode className="h-4 w-4 text-muted-foreground" />
              Nova versão — {type.label}
            </DialogTitle>
            <DialogDescription>
              A versão nasce inativa. Nada muda até alguém ativá-la.
              {(type.value === "extraction" || isBuildType(type.value)) && " O contrato de saída em JSON é fixo no código e sempre anexado: escreva só as instruções."}
            </DialogDescription>
          </DialogHeader>

          <div className="space-y-3 py-4">
            <div className="grid grid-cols-1 sm:grid-cols-3 gap-3">
              <div className="space-y-1">
                <label htmlFor="prompt-version" className="text-xs font-medium text-foreground">Versão</label>
                <Input id="prompt-version" value={version} onChange={e => setVersion(e.target.value)}
                  placeholder="ex.: 1.1.0" maxLength={VERSION_MAX + 5} className="h-9 font-mono text-sm" autoFocus />
              </div>
              <div className="space-y-1 sm:col-span-2">
                <label htmlFor="prompt-description" className="text-xs font-medium text-foreground">Descrição</label>
                <Input id="prompt-description" value={description} onChange={e => setDescription(e.target.value)}
                  placeholder="O que muda nesta versão" className="h-9 text-sm" />
              </div>
            </div>
            <div className="space-y-1">
              <div className="flex items-center justify-between">
                <label htmlFor="prompt-content" className="text-xs font-medium text-foreground">Conteúdo</label>
                <span
                  data-testid="prompt-content-counter"
                  className={`text-[11px] tabular-nums ${content.length > CONTENT_MAX ? "text-destructive" : "text-muted-foreground"}`}
                >
                  {content.length.toLocaleString("pt-BR")} / {CONTENT_MAX.toLocaleString("pt-BR")}
                </span>
              </div>
              <Textarea id="prompt-content" value={content} onChange={e => setContent(e.target.value)}
                rows={12} className="font-mono text-xs" spellCheck={false} />
            </div>
            {problem && <p className="text-[11px] text-destructive">{problem}</p>}
            {failure && (
              <p role="alert" className="flex items-start gap-1.5 rounded-md border border-destructive/30 bg-destructive/5 px-3 py-2 text-xs text-destructive">
                <ShieldAlert className="h-3.5 w-3.5 shrink-0 mt-0.5" />{failure}
              </p>
            )}
          </div>

          <DialogFooter className="gap-2">
            <Button type="button" variant="outline" size="sm" onClick={close} disabled={create.isPending}>Cancelar</Button>
            <Button type="submit" size="sm" disabled={!ready || create.isPending} className="min-w-[110px]">
              {create.isPending
                ? <RefreshCw className="h-3.5 w-3.5 animate-spin" />
                : <><Save className="h-3.5 w-3.5 mr-1.5" />Criar versão</>}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  );
}

// ── Version row ────────────────────────────────────────────────────────────────

function rowTone(v: PromptVersion, expanded: boolean): string {
  if (v.isActive) return "bg-primary/5";
  if (expanded) return "bg-muted/30";
  return "";
}

function VersionRow({
  v,
  onActivate,
  isActivating,
}: Readonly<{ v: PromptVersion; onActivate: (v: PromptVersion) => void; isActivating: boolean }>) {
  const [expanded, setExpanded] = useState(false);

  return (
    <>
      <tr aria-label={`Versão ${v.version}`} className={`transition-colors ${rowTone(v, expanded)}`}>
        <td className="px-4 py-3">
          <div className="flex items-center gap-2">
            {v.isActive && <CheckCircle2 className="h-3.5 w-3.5 text-primary shrink-0" />}
            <span className="text-sm font-medium text-foreground tabular-nums">v{v.version}</span>
            {v.isActive && (
              <span className="text-[10px] font-medium bg-primary/10 text-primary px-1.5 py-0.5 rounded">ATIVA</span>
            )}
          </div>
        </td>
        <td className="px-4 py-3">
          <span className="font-mono text-xs text-muted-foreground break-all">{v.hash}</span>
        </td>
        <td className="px-4 py-3 text-xs text-muted-foreground">{v.createdBy}</td>
        <td className="px-4 py-3 text-xs text-muted-foreground tabular-nums whitespace-nowrap">
          <span className="flex items-center gap-1.5"><Clock className="h-3 w-3 shrink-0" />{formatDateTime(v.createdAt)}</span>
        </td>
        <td className="px-4 py-3">
          {!v.isActive && (
            <Button variant="outline" size="sm" className="h-7 text-xs px-2" disabled={isActivating}
              aria-label={`Ativar versão ${v.version}`} onClick={() => onActivate(v)}>
              Ativar
            </Button>
          )}
        </td>
        <td className="px-2 py-3">
          <Button variant="ghost" size="sm" className="h-7 w-7 p-0 text-muted-foreground"
            aria-expanded={expanded} aria-label={expanded ? `Ocultar versão ${v.version}` : `Ver versão ${v.version}`}
            onClick={() => setExpanded(x => !x)}>
            {expanded ? <ChevronUp className="h-3.5 w-3.5" /> : <ChevronDown className="h-3.5 w-3.5" />}
          </Button>
        </td>
      </tr>

      {expanded && (
        <tr className="bg-muted/20">
          <td colSpan={6} className="px-6 py-4">
            <div className="space-y-3">
              {v.description && (
                <div>
                  <p className="text-xs text-muted-foreground mb-1">Descrição</p>
                  <p className="text-sm text-foreground">{v.description}</p>
                </div>
              )}
              <div>
                <p className="text-xs text-muted-foreground mb-1 flex items-center gap-1">
                  <FileCode className="h-3 w-3" /> Início do conteúdo
                </p>
                <pre className="text-xs font-mono text-muted-foreground bg-muted rounded-md px-3 py-2 whitespace-pre-wrap overflow-x-auto max-h-48">
                  {v.content || "—"}
                </pre>
              </div>
            </div>
          </td>
        </tr>
      )}
    </>
  );
}

// ── Page ───────────────────────────────────────────────────────────────────────

export default function AiPromptsPage() {
  const typesQuery = usePromptTypes();
  const promptTypes: PromptTypeOption[] = typesQuery.data && typesQuery.data.length > 0
    ? typesQuery.data.map(t => ({ value: t.type, label: PROMPT_TYPE_LABEL[t.type] ?? t.type, usedByRuntime: t.usedByRuntime, builtInHash: t.builtInHash }))
    : FALLBACK_TYPES;
  const [selected, setSelected] = useState<PromptType>("extraction");
  const type = promptTypes.find(t => t.value === selected) ?? promptTypes[0];
  const { data: versions, isLoading, isError, error } = usePromptVersions(selected);
  const activate = useActivatePromptVersion();
  const [creating, setCreating] = useState(false);
  const [confirming, setConfirming] = useState<PromptVersion | null>(null);

  const activeVersion = versions?.find(v => v.isActive);

  const runActivation = () => {
    if (!confirming) return;
    const target = confirming;
    activate.mutate({ promptType: selected, versionId: target.id }, {
      onSuccess: (r) => {
        toast.success(`Versão ${r.version} ativada. Hash ${r.hash}.`);
        setConfirming(null);
      },
      onError: (err) => {
        // a 409 means another activation won: the list is refetched (onSettled) and the message says so
        const text = err instanceof ApiError && err.status === 409
          ? (err.serverMessage ?? CONCURRENT_ACTIVATION)
          : governedErrorText(err);
        toast.error(text);
        setConfirming(null);
      },
    });
  };

  return (
    <div className="p-4 sm:p-6 space-y-5 max-w-5xl">
      <div>
        <h1 className="font-display text-[20px] font-bold text-foreground tracking-tight">Prompts de IA</h1>
        <p className="text-sm text-muted-foreground mt-0.5">
          Versões de cada prompt: criação, ativação e volta a uma versão anterior.
        </p>
      </div>

      <div className="bg-card border border-border rounded-lg p-4 space-y-3">
        <div className="flex flex-wrap items-center justify-between gap-3">
          <div role="tablist" aria-label="Tipo de prompt" className="flex flex-wrap gap-2">
            {promptTypes.map(pt => (
              <button
                key={pt.value}
                type="button"
                role="tab"
                aria-selected={selected === pt.value}
                onClick={() => setSelected(pt.value)}
                className={`px-3 py-1.5 rounded-md text-xs font-medium border transition-colors ${
                  selected === pt.value
                    ? "bg-primary text-primary-foreground border-primary"
                    : "bg-background text-muted-foreground border-border hover:text-foreground hover:bg-muted"
                }`}
              >
                {pt.label}
              </button>
            ))}
          </div>
          <Button size="sm" className="h-8 text-xs gap-1.5" onClick={() => setCreating(true)}>
            <Plus className="h-3.5 w-3.5" /> Nova versão
          </Button>
        </div>

        {/* What the runtime really does with this type */}
        {type.usedByRuntime ? (
          <div data-testid="prompt-runtime-usage" className="flex items-start gap-2 rounded-md border border-green-500/30 bg-green-500/5 px-3 py-2">
            <Zap className="h-3.5 w-3.5 text-green-700 shrink-0 mt-0.5" />
            <p className="text-xs text-foreground">
              <span className="font-medium">Usado pelo runtime.</span>{" "}
              {isBuildType(type.value)
                ? "A versão ativa é o texto de instruções que o Orken Build envia ao provedor de IA nesta capacidade (sem versão ativa, vale o texto embutido)."
                : "A versão ativa é o texto de instruções enviado ao analisador Claude."}
              {" "}O contrato de saída em JSON é fixo no código e sempre anexado ao final.
              {type.builtInHash && (
                <span className="block mt-0.5 text-muted-foreground">
                  Hash do texto embutido: <span className="font-mono">{type.builtInHash}</span>
                </span>
              )}
            </p>
          </div>
        ) : (
          <div data-testid="prompt-runtime-usage" className="flex items-start gap-2 rounded-md border border-border bg-muted/40 px-3 py-2">
            <CircleOff className="h-3.5 w-3.5 text-muted-foreground shrink-0 mt-0.5" />
            <p className="text-xs text-foreground">
              <span className="font-medium">Ainda não usado pelo runtime.</span> As versões ficam guardadas e versionadas,
              mas nenhuma parte do Orken usa este prompt hoje: ativar uma versão não muda nenhuma análise.
            </p>
          </div>
        )}

        {activeVersion && (
          <div className="flex flex-wrap items-center gap-x-3 gap-y-1 pt-2 border-t border-border">
            <CheckCircle2 className="h-4 w-4 text-primary shrink-0" />
            <span className="text-xs text-muted-foreground">Versão ativa:</span>
            <span className="text-xs font-semibold text-foreground">v{activeVersion.version}</span>
            <span className="text-xs text-muted-foreground font-mono break-all">
              <Hash className="h-3 w-3 inline-block mr-0.5" />{activeVersion.hash}
            </span>
          </div>
        )}
        {versions && versions.length > 0 && !activeVersion && (
          <p className="text-xs text-muted-foreground pt-2 border-t border-border">Nenhuma versão ativa deste tipo.</p>
        )}
      </div>

      {isError && <ApiErrorPanel error={error} title="Não foi possível carregar os prompts" />}

      <div className="bg-card border border-border rounded-lg overflow-hidden">
        <div className="px-4 py-3 border-b border-border bg-muted/30">
          <h2 className="text-sm font-medium text-foreground">
            Histórico de versões
            {versions && <span className="ml-2 text-muted-foreground font-normal">({versions.length})</span>}
          </h2>
        </div>

        {isLoading && <div className="p-4 sm:p-8 text-center text-sm text-muted-foreground">Carregando...</div>}
        {versions && versions.length === 0 && (
          <div className="p-4 sm:p-8 text-center text-sm text-muted-foreground">Nenhuma versão deste prompt.</div>
        )}
        {versions && versions.length > 0 && (
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-border bg-muted/20">
                  {["Versão", "Hash", "Autor", "Criada em", "Ação", ""].map(h => (
                    <th key={h} className="text-left px-4 py-2 text-xs text-muted-foreground font-medium whitespace-nowrap">{h}</th>
                  ))}
                </tr>
              </thead>
              <tbody className="divide-y divide-border">
                {versions.map(v => (
                  <VersionRow key={v.id} v={v} onActivate={setConfirming} isActivating={activate.isPending} />
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      <div className="flex items-start gap-3 rounded-lg border border-border bg-muted/30 px-4 py-3">
        <Hash className="h-4 w-4 text-muted-foreground shrink-0 mt-0.5" />
        <p className="text-xs text-muted-foreground">
          O hash é o SHA-256 do conteúdo (os 16 primeiros caracteres hexadecimais) e é o que a telemetria registra em cada
          análise. Só uma versão fica ativa por tipo; voltar a uma versão anterior é ativá-la de novo. Criação e ativação
          ficam registradas na auditoria.
        </p>
      </div>

      <NewVersionDialog key={selected} type={type} open={creating} onClose={() => setCreating(false)} />

      <AlertDialog open={!!confirming} onOpenChange={(o) => { if (!o && !activate.isPending) setConfirming(null); }}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Ativar a versão {confirming?.version} de {type.label}?</AlertDialogTitle>
            <AlertDialogDescription>
              {type.usedByRuntime
                ? "O runtime passa a usar esta versão imediatamente."
                : "Ela passa a ser a versão ativa deste tipo. Ainda não usado pelo runtime: nenhuma análise muda."}
              {confirming && <span className="block mt-1 font-mono text-xs break-all">Hash {confirming.hash}</span>}
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={activate.isPending}>Cancelar</AlertDialogCancel>
            <AlertDialogAction disabled={activate.isPending} onClick={(e) => { e.preventDefault(); runActivation(); }}>
              {activate.isPending ? <RefreshCw className="h-3.5 w-3.5 animate-spin" /> : "Ativar versão"}
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </div>
  );
}
