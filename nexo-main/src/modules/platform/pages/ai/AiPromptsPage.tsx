import { useState } from "react";
import { FileCode, CheckCircle2, Clock, Hash, ChevronDown, ChevronUp } from "lucide-react";
import { Button } from "@/components/ui/button";
import { usePromptVersions, useSetActivePrompt } from "../../hooks/useAiOperations";
import type { PromptVersion } from "../../types/aiOperations";

const PROMPT_TYPES = [
  { value: "extraction",     label: "Extraction" },
  { value: "interpretation", label: "Interpretation" },
  { value: "memory",         label: "Memory Profile" },
];

function VersionRow({
  v,
  onActivate,
  isActivating,
}: {
  v: PromptVersion;
  onActivate: (id: string) => void;
  isActivating: boolean;
}) {
  const [expanded, setExpanded] = useState(false);

  const ts = new Date(v.createdAt).toLocaleString("pt-BR", {
    day: "2-digit", month: "2-digit", year: "2-digit",
    hour: "2-digit", minute: "2-digit",
  });

  return (
    <>
      <tr
        onClick={() => setExpanded(x => !x)}
        className={`cursor-pointer transition-colors hover:bg-muted/50 ${
          v.isActive ? "bg-primary/5" : expanded ? "bg-muted/30" : ""
        }`}
      >
        {/* Version */}
        <td className="px-4 py-3">
          <div className="flex items-center gap-2">
            {v.isActive && (
              <CheckCircle2 className="h-3.5 w-3.5 text-primary shrink-0" />
            )}
            <span className="text-sm font-medium text-foreground tabular-nums">
              v{v.version}
            </span>
            {v.isActive && (
              <span className="text-[10px] font-medium bg-primary/10 text-primary px-1.5 py-0.5 rounded">
                ATIVO
              </span>
            )}
          </div>
        </td>

        {/* Hash */}
        <td className="px-4 py-3">
          <span className="font-mono text-xs text-muted-foreground">{v.hash}</span>
        </td>

        {/* Author */}
        <td className="px-4 py-3 text-xs text-muted-foreground">{v.createdBy}</td>

        {/* Date */}
        <td className="px-4 py-3 text-xs text-muted-foreground tabular-nums whitespace-nowrap">
          <span className="flex items-center gap-1.5">
            <Clock className="h-3 w-3 shrink-0" />
            {ts}
          </span>
        </td>

        {/* Actions */}
        <td className="px-4 py-3">
          <div className="flex items-center gap-2" onClick={e => e.stopPropagation()}>
            {!v.isActive && (
              <Button
                variant="outline"
                size="sm"
                className="h-6 text-xs px-2"
                disabled={isActivating}
                onClick={() => onActivate(v.id)}
              >
                Ativar
              </Button>
            )}
          </div>
        </td>

        {/* Expand chevron */}
        <td className="px-4 py-3 text-muted-foreground">
          {expanded
            ? <ChevronUp className="h-3.5 w-3.5" />
            : <ChevronDown className="h-3.5 w-3.5" />}
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
              {v.content ? (
                <div>
                  <p className="text-xs text-muted-foreground mb-1 flex items-center gap-1">
                    <FileCode className="h-3 w-3" /> Conteúdo do Prompt
                  </p>
                  <pre className="text-xs font-mono text-muted-foreground bg-muted rounded-md px-3 py-2 whitespace-pre-wrap overflow-x-auto max-h-48">
                    {v.content}
                  </pre>
                </div>
              ) : (
                <p className="text-xs text-muted-foreground italic">
                  Conteúdo não disponível — ative{" "}
                  <span className="font-mono">EnablePromptLogging</span> nas Feature Flags.
                </p>
              )}
            </div>
          </td>
        </tr>
      )}
    </>
  );
}

export default function AiPromptsPage() {
  const [selectedType, setSelectedType] = useState(PROMPT_TYPES[0].value);
  const { data: versions, isLoading } = usePromptVersions(selectedType);
  const setActiveMut = useSetActivePrompt();

  const handleActivate = (id: string) => {
    setActiveMut.mutate({ promptType: selectedType, versionId: id });
  };

  const activeVersion = versions?.find(v => v.isActive);

  return (
    <div className="p-6 space-y-5 max-w-5xl">

      {/* Header */}
      <div>
        <h1 className="font-display text-[20px] font-bold text-foreground tracking-tight">Gestão de Prompts</h1>
        <p className="text-sm text-muted-foreground mt-0.5">
          Versões de prompts por tipo de operação — histórico, ativação e rollback.
        </p>
      </div>

      {/* Type selector */}
      <div className="bg-card border border-border rounded-lg p-4 space-y-3">
        <h2 className="text-sm font-medium text-foreground">Tipo de Prompt</h2>
        <div className="flex flex-wrap gap-2">
          {PROMPT_TYPES.map(pt => (
            <button
              key={pt.value}
              onClick={() => setSelectedType(pt.value)}
              className={`px-3 py-1.5 rounded-md text-xs font-medium border transition-colors ${
                selectedType === pt.value
                  ? "bg-primary text-primary-foreground border-primary"
                  : "bg-background text-muted-foreground border-border hover:text-foreground hover:bg-muted"
              }`}
            >
              {pt.label}
            </button>
          ))}
        </div>

        {/* Active version summary */}
        {!isLoading && activeVersion && (
          <div className="flex items-center gap-3 pt-1 border-t border-border mt-3">
            <CheckCircle2 className="h-4 w-4 text-primary shrink-0" />
            <div>
              <span className="text-xs text-muted-foreground">Versão ativa: </span>
              <span className="text-xs font-semibold text-foreground">v{activeVersion.version}</span>
              <span className="text-xs text-muted-foreground ml-2 font-mono">
                <Hash className="h-3 w-3 inline-block mr-0.5" />{activeVersion.hash}
              </span>
            </div>
          </div>
        )}
      </div>

      {/* Versions table */}
      <div className="bg-card border border-border rounded-lg overflow-hidden">
        <div className="px-4 py-3 border-b border-border bg-muted/30 flex items-center justify-between">
          <h2 className="text-sm font-medium text-foreground">
            Histórico de versões
            {versions && (
              <span className="ml-2 text-muted-foreground font-normal">
                ({versions.length} versões)
              </span>
            )}
          </h2>
        </div>

        {isLoading ? (
          <div className="p-8 text-center text-sm text-muted-foreground">Carregando...</div>
        ) : !versions?.length ? (
          <div className="p-8 text-center text-sm text-muted-foreground">
            Nenhuma versão encontrada para este tipo de prompt.
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-sm">
              <thead>
                <tr className="border-b border-border bg-muted/20">
                  {["Versão", "Hash", "Autor", "Criado em", "Ação", ""].map(h => (
                    <th
                      key={h}
                      className="text-left px-4 py-2 text-xs text-muted-foreground font-medium whitespace-nowrap"
                    >
                      {h}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody className="divide-y divide-border">
                {versions.map(v => (
                  <VersionRow
                    key={v.id}
                    v={v}
                    onActivate={handleActivate}
                    isActivating={setActiveMut.isPending}
                  />
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {/* Info notice */}
      <div className="flex items-start gap-3 bg-blue-50 border border-blue-200 rounded-lg px-4 py-3">
        <FileCode className="h-4 w-4 text-blue-600 shrink-0 mt-0.5" />
        <div>
          <p className="text-xs font-medium text-blue-800">
            Rollback imediato — ativar uma versão anterior é instantâneo.
          </p>
          <p className="text-xs text-blue-700 mt-0.5">
            Apenas uma versão pode estar ativa por tipo de prompt. Todas as alterações são
            registradas no audit trail com o email do operador.
          </p>
        </div>
      </div>
    </div>
  );
}
