import { useSearchParams } from "react-router-dom";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { TaxModuleSection } from "./tax/TaxModuleSection";
import { TaxPilotSection } from "./tax/TaxPilotSection";
import { TaxRulesSection } from "./tax/TaxRulesSection";
import {
  TaxAuditSection, TaxClassificationsSection, TaxDocumentsSection, TaxObligationsSection, TaxRetentionSection, TaxSourcesSection,
} from "./tax/TaxReferenceSections";

const TABS = [
  { key: "estado", label: "Estado e ativação", element: <TaxModuleSection /> },
  { key: "piloto", label: "Piloto", element: <TaxPilotSection /> },
  { key: "regras", label: "Regras e revisões", element: <TaxRulesSection /> },
  { key: "fontes", label: "Fontes", element: <TaxSourcesSection /> },
  { key: "classificacao", label: "Classificação", element: <TaxClassificationsSection /> },
  { key: "obrigacoes", label: "Obrigações", element: <TaxObligationsSection /> },
  { key: "retencao", label: "Retenção", element: <TaxRetentionSection /> },
  { key: "documentos", label: "Documentos", element: <TaxDocumentsSection /> },
  { key: "auditoria", label: "Recusas e auditoria", element: <TaxAuditSection /> },
] as const;

/**
 * Inteligência tributária na plataforma: estado e ativação do módulo, piloto, catálogo de regras com aprovação interna e
 * revisões profissionais (registros separados), fontes, catálogos, retenção, documentos, recusas e auditoria.
 * Ler exige sessão de plataforma; alterar exige super admin — o servidor decide e a tela explica a recusa.
 */
export default function PlatformTaxPage() {
  const [params, setParams] = useSearchParams();
  const tab = TABS.some((t) => t.key === params.get("aba")) ? params.get("aba")! : "estado";
  return (
    <div className="space-y-6 p-4 sm:p-6">
      <header className="space-y-1">
        <p className="text-xs font-medium uppercase tracking-wide text-muted-foreground">Plataforma</p>
        <h1 className="text-2xl font-semibold">Inteligência tributária</h1>
        <p className="text-sm text-muted-foreground">
          Nada aqui muda a configuração da instalação. Ativar, aprovar ou registrar revisão são atos auditados, com motivo e referência.
        </p>
      </header>
      <Tabs value={tab} onValueChange={(v) => setParams({ aba: v }, { replace: true })}>
        <div className="-mx-1 overflow-x-auto px-1">
          <TabsList className="w-max">
            {TABS.map((t) => <TabsTrigger key={t.key} value={t.key}>{t.label}</TabsTrigger>)}
          </TabsList>
        </div>
        {TABS.map((t) => <TabsContent key={t.key} value={t.key} className="mt-4">{t.element}</TabsContent>)}
      </Tabs>
    </div>
  );
}
