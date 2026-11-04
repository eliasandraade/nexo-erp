import { useState } from "react";
import { Sparkles } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import type { BuildModelVersionDto } from "../../api/build-twin.api";
import { useActiveSuggestion, useAiGate, useGenerateModelMapping } from "../../hooks/use-build-ai";
import { aiErrorMessage, readMapperSuggestion } from "../../lib/ai";
import { AiErrorAlert, AiPanel, ConfidenceBar } from "./common";
import { AiSuggestionCard, type SelectableItem } from "./AiSuggestionCard";

/**
 * "Sugerir vínculos" (model mapper): for a Ready version, proposes stage ↔ category/zone bindings from the categories and
 * zones of the model (never the element list). Without a provider the IFC-class table answers (badge "Regras").
 */
export function ModelMapperAi({ projectId, version }: { projectId: string; version: BuildModelVersionDto | null }) {
  const gate = useAiGate();
  const generate = useGenerateModelMapping(projectId);
  const { suggestion, setCurrent } = useActiveSuggestion(projectId, "ModelMapper",
    version ? (s) => !s.subjectId || s.subjectId === version.id : undefined);
  const [error, setError] = useState<string | null>(null);

  const out = suggestion ? readMapperSuggestion(suggestion.output) : null;
  const bindings = out?.bindings ?? [];
  const groups: Array<{ title: string; scope: string }> = [
    { title: "Por categoria", scope: "Category" }, { title: "Por zona", scope: "Zone" },
  ];
  const items: SelectableItem[] = groups.flatMap((g) => bindings.filter((b) => (b.scope === "Zone" ? "Zone" : "Category") === g.scope)
    .map((b, idx) => {
      const target = b.scope === "Zone" ? (b.zoneName ?? "Zona") : (b.category ?? "Categoria");
      return {
        id: b.id,
        label: `Vincular ${target} a ${b.stageName}`,
        content: (
          <div className="space-y-0.5 text-sm">
            {idx === 0 && <p className="text-[11px] font-semibold uppercase tracking-wide text-muted-foreground">{g.title}</p>}
            <div className="flex flex-wrap items-center justify-between gap-2">
              <p><span className="font-mono text-xs">{target}</span> → <span className="font-medium">{b.stageName}</span></p>
              <ConfidenceBar value={b.confidence} compact label={`Confiança: ${target}`} />
            </div>
            {b.rationale && <p className="text-xs text-muted-foreground">{b.rationale}</p>}
          </div>
        ),
      };
    }));

  const submit = () => {
    if (!version) return;
    setError(null);
    generate.mutate({ modelVersionId: version.id }, {
      onSuccess: (s) => setCurrent(s),
      onError:   (e) => setError(aiErrorMessage(e, "Não foi possível sugerir vínculos.")),
    });
  };

  const pending = suggestion?.status === "Pending";

  return (
    <AiPanel testId="model-mapper" title="Sugerir vínculos" aiEnabled={gate.aiEnabled} loading={gate.loading}
      description="Propõe vínculos entre etapas e categorias/zonas do modelo pronto. Sem provedor de IA, usa a tabela de classes IFC (regras).">
      {!pending && (
        <div className="flex flex-wrap items-center gap-2">
          <Button size="sm" onClick={submit} disabled={!gate.aiEnabled || !version || generate.isPending}>
            <Sparkles className="mr-1 h-3.5 w-3.5" /> {generate.isPending ? "Sugerindo…" : "Sugerir vínculos"}
          </Button>
          {!version && <span className="text-xs text-muted-foreground">Precisa de uma versão do modelo pronta.</span>}
          {version && <span className="text-xs text-muted-foreground">Versão {version.number} · {version.sourceFileName}</span>}
        </div>
      )}
      <AiErrorAlert message={error} />
      {suggestion && (
        <AiSuggestionCard suggestion={suggestion} items={items} acceptLabel="Criar vínculos"
          acceptHint="Aceitar cria os vínculos selecionados (vínculos repetidos são ignorados). Você pode removê-los depois na lista de vínculos."
          onDecided={(s) => { setCurrent(s); toast.success(s.status === "Rejected" ? "Sugestão recusada." : "Vínculos criados."); }}>
          {items.length === 0 && <p className="text-xs text-muted-foreground">Nenhum vínculo a sugerir para esta versão.</p>}
        </AiSuggestionCard>
      )}
      {suggestion && !pending && (
        <div className="flex justify-end"><Button size="sm" variant="ghost" onClick={() => setCurrent(null)}>Fechar</Button></div>
      )}
    </AiPanel>
  );
}
