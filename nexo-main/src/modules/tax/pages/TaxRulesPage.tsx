import { useEffect, useState } from "react";
import { useSearchParams } from "react-router-dom";
import { useQuery } from "@tanstack/react-query";
import { BookOpen, ExternalLink, ScrollText } from "lucide-react";
import { PageHeader } from "@/components/shared/PageHeader";
import { SectionCard } from "@/components/shared/SectionCard";
import { ErrorState } from "@/components/shared/ErrorState";
import { EmptyState } from "@/components/shared/EmptyState";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Skeleton } from "@/components/ui/skeleton";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { formatDateTime } from "@/lib/formatters";
import { cn } from "@/lib/utils";
import { TAX_KEYS, listLegalSources, listTaxRules, type TaxLegalSourceDto, type TaxRuleDto } from "../api/tax.api";
import { INTERNAL_APPROVAL, INTERNAL_APPROVAL_NOTICE, PROFESSIONAL_REVIEW, RULE_STATUS, TEXT_VERIFICATION, labelOf, toneOf } from "../lib/labels";
import { formatIsoDate } from "../lib/forms";
import { ToneBadge } from "../components/TaxBadges";
import { SourceLinks } from "../components/EngineResult";
import { TaxNote, TaxSubNav } from "../components/TaxShell";

/** Regras e fontes: o catálogo usado pelos cálculos (somente leitura — mantido pela plataforma) e o texto literal da lei. */

/** " · conferida por X em data" — who compared the excerpt with the official text, and when. */
function verificationText(verifiedAt: string | null, verifiedBy: string | null): string {
  if (!verifiedAt) return "";
  const by = verifiedBy ? `por ${verifiedBy} ` : "";
  return ` · conferida ${by}em ${formatDateTime(verifiedAt)}`;
}

export default function TaxRulesPage() {
  const [params, setParams] = useSearchParams();
  const source = params.get("fonte");
  const tab = source || params.get("aba") === "fontes" ? "fontes" : "regras";

  return (
    <div className="space-y-6">
      <PageHeader
        eyebrow="Inteligência tributária"
        title="Regras e fontes"
        description="Cada regra aplicada tem versão, vigência e a fonte jurídica de onde vem. Regras pendentes de regulamentação aparecem, mas não são aplicadas."
      />
      <TaxSubNav />
      <TaxNote>
        As regras são mantidas pela plataforma Orken e nunca editadas: uma mudança vira uma nova versão, e cada cálculo guarda a versão que usou.
        “Texto oficial conferido” quer dizer só que o trecho reproduz a lei; a revisão da interpretação por um profissional tributário é um estado à parte.
        “Aprovado internamente para piloto controlado” é uma decisão de produto e governança da Andrade Systems, não um parecer profissional: as duas
        aprovações aparecem lado a lado e uma não substitui a outra.
      </TaxNote>
      <Tabs value={tab} onValueChange={(v) => setParams(v === "fontes" ? { aba: "fontes" } : {})}>
        <TabsList>
          <TabsTrigger value="regras">Regras</TabsTrigger>
          <TabsTrigger value="fontes">Fontes jurídicas</TabsTrigger>
        </TabsList>
        <TabsContent value="regras"><Rules /></TabsContent>
        <TabsContent value="fontes"><Sources highlight={source} onClear={() => setParams({ aba: "fontes" })} /></TabsContent>
      </Tabs>
    </div>
  );
}

function Rules() {
  const rules = useQuery({ queryKey: TAX_KEYS.rules, queryFn: listTaxRules });
  if (rules.isLoading) return <Skeleton className="h-48 w-full" />;
  if (rules.isError) return <ErrorState title="Não foi possível ler as regras" onRetry={() => void rules.refetch()} />;
  if (!rules.data || rules.data.length === 0) return <EmptyState icon={ScrollText} title="Nenhuma regra no catálogo" />;
  return <div className="space-y-4">{rules.data.map((r) => <Rule key={r.id} rule={r} />)}</div>;
}

function Rule({ rule }: Readonly<{ rule: TaxRuleDto }>) {
  const versions = [...rule.versions].sort((a, b) => b.version - a.version);
  return (
    <SectionCard title={rule.name} description={rule.description}>
      <p className="-mt-2 mb-3 font-mono text-xs text-muted-foreground">{rule.key}</p>
      <ul className="space-y-3">
        {versions.map((v) => (
          <li key={v.id} className="rounded-md border border-border p-3 text-sm">
            <div className="flex flex-wrap items-center gap-2">
              <span className="font-medium">Versão {v.version}</span>
              <ToneBadge label={labelOf(RULE_STATUS, v.status)} tone={toneOf(RULE_STATUS, v.status)} title="Situação técnica no motor de cálculo" />
              <ToneBadge
                label={labelOf(PROFESSIONAL_REVIEW, v.professionalReview)}
                tone={toneOf(PROFESSIONAL_REVIEW, v.professionalReview)}
                title="Revisão da interpretação por profissional tributário — estar em vigor no motor não é aprovação profissional"
              />
              {v.internalApproval.status !== "NotApproved" && (
                <ToneBadge
                  label={labelOf(INTERNAL_APPROVAL, v.internalApproval.status)}
                  tone={toneOf(INTERNAL_APPROVAL, v.internalApproval.status)}
                  title={INTERNAL_APPROVAL_NOTICE}
                />
              )}
              <span className="text-xs text-muted-foreground">
                {formatIsoDate(v.validFrom)} até {v.validTo ? formatIsoDate(v.validTo) : "sem fim definido"} · prioridade {v.priority}
              </span>
            </div>
            {v.notes && <p className="mt-1 whitespace-pre-line text-muted-foreground">{v.notes}</p>}
            {v.professionalReviewedBy && (
              <div className="mt-1 space-y-0.5 text-xs text-muted-foreground">
                <p>
                  Revisão profissional por {v.professionalReviewedBy}{v.professionalReference ? ` (${v.professionalReference})` : ""}
                  {v.professionalReviewedOn ? ` (revisão de ${formatIsoDate(v.professionalReviewedOn)})` : ""}{v.professionalReviewNotes ? `: ${v.professionalReviewNotes}` : ""}
                </p>
                {v.professionalReviewRecordedBy && (
                  <p>
                    Registrado na plataforma por {v.professionalReviewRecordedBy}{v.professionalReviewRecordedByRole ? ` (${v.professionalReviewRecordedByRole})` : ""}
                    {v.professionalReviewedAt ? ` em ${formatDateTime(v.professionalReviewedAt)}` : ""} — quem registra não é quem revisou.
                  </p>
                )}
                {v.professionalReviewRegimes && v.professionalReviewRegimes.length > 0 && <p>Regimes cobertos: {v.professionalReviewRegimes.join(", ")}</p>}
                {v.professionalReviewScope && <p>Escopo da revisão: {v.professionalReviewScope}</p>}
                {v.professionalReviewValidFrom && (
                  <p>Vale de {formatIsoDate(v.professionalReviewValidFrom)}{v.professionalReviewValidTo ? ` a ${formatIsoDate(v.professionalReviewValidTo)}` : " sem fim definido"}</p>
                )}
                {v.professionalReviewAssumptions && <p>Premissas: {v.professionalReviewAssumptions}</p>}
                {v.professionalReviewCaveats && <p className="text-warning">Ressalvas: {v.professionalReviewCaveats}</p>}
              </div>
            )}
            {v.internalApproval.status !== "NotApproved" && (
              <div className="mt-1 space-y-0.5 text-xs text-muted-foreground">
                <p>
                  {labelOf(INTERNAL_APPROVAL, v.internalApproval.status)}
                  {v.internalApproval.approvedBy ? ` por ${v.internalApproval.approvedBy}` : ""}
                  {v.internalApproval.approvedAt ? ` em ${formatDateTime(v.internalApproval.approvedAt)}` : ""}
                  {v.internalApproval.policy ? ` · política ${v.internalApproval.policy}` : ""}
                </p>
                <p>{INTERNAL_APPROVAL_NOTICE}</p>
                {v.internalApproval.validFrom && (
                  <p>Vale para o piloto de {formatIsoDate(v.internalApproval.validFrom)}{v.internalApproval.validTo ? ` a ${formatIsoDate(v.internalApproval.validTo)}` : " sem fim definido"}</p>
                )}
                {v.internalApproval.caveats && <p className="text-warning">Ressalvas aceitas: {v.internalApproval.caveats}</p>}
                {v.internalApproval.withdrawalReason && <p className="text-destructive">Retirada: {v.internalApproval.withdrawalReason}</p>}
              </div>
            )}
            <p className="mt-1 text-xs">Fonte: <SourceLinks codes={v.legalSourceCodes} /></p>
            <p className="mt-1 break-all font-mono text-[11px] text-muted-foreground">conteúdo {v.contentHash} · origem {v.origin} · {v.createdBy}{v.activatedAt ? ` · em vigor desde ${formatDateTime(v.activatedAt)}` : ""}</p>
            <details className="mt-2">
              <summary className="cursor-pointer text-xs font-medium">Condições e resultado</summary>
              <pre className="mt-1 max-h-72 overflow-auto rounded bg-muted/50 p-2 text-[11px]">{JSON.stringify({ conditions: v.conditions, result: v.result }, null, 2)}</pre>
            </details>
          </li>
        ))}
      </ul>
    </SectionCard>
  );
}

function Sources({ highlight, onClear }: Readonly<{ highlight: string | null; onClear: () => void }>) {
  const sources = useQuery({ queryKey: TAX_KEYS.legalSources, queryFn: listLegalSources });
  const [search, setSearch] = useState("");

  useEffect(() => {
    if (!highlight || !sources.data) return;
    document.getElementById(`fonte-${highlight}`)?.scrollIntoView({ block: "start" });
  }, [highlight, sources.data]);

  if (sources.isLoading) return <Skeleton className="h-48 w-full" />;
  if (sources.isError) return <ErrorState title="Não foi possível ler as fontes jurídicas" onRetry={() => void sources.refetch()} />;
  if (!sources.data || sources.data.length === 0) return <EmptyState icon={BookOpen} title="Nenhuma fonte cadastrada" />;

  const q = search.trim().toLowerCase();
  const visible = sources.data.filter((s) => !q || `${s.code} ${s.title} ${s.reference} ${s.excerpt}`.toLowerCase().includes(q));

  return (
    <div className="space-y-4">
      <div className="flex flex-col gap-2 sm:flex-row sm:items-center">
        <Input aria-label="Buscar fonte" placeholder="Buscar por código, título ou texto" value={search} onChange={(e) => setSearch(e.target.value)} className="sm:max-w-sm" />
        {highlight && <Button variant="ghost" size="sm" onClick={onClear}>Tirar destaque de {highlight}</Button>}
      </div>
      {visible.map((s) => <Source key={s.id} s={s} active={s.code === highlight} />)}
      {visible.length === 0 && <p className="text-sm text-muted-foreground">Nenhuma fonte com esse texto.</p>}
    </div>
  );
}

function Source({ s, active }: Readonly<{ s: TaxLegalSourceDto; active: boolean }>) {
  return (
    <section id={`fonte-${s.code}`} className={cn("data-region scroll-mt-20 p-5", active && "ring-2 ring-primary/60")}>
      <div className="flex flex-wrap items-center gap-2">
        <code className="text-xs font-semibold">{s.code}</code>
        <ToneBadge
          label={labelOf(TEXT_VERIFICATION, s.textVerification)}
          tone={toneOf(TEXT_VERIFICATION, s.textVerification)}
          title="Confere só que o trecho reproduz o texto oficial; não é parecer jurídico"
        />
      </div>
      <h3 className="section-heading mt-1">{s.title}</h3>
      <p className="text-sm text-muted-foreground">
        {s.reference}{s.publishedOn ? ` · publicada em ${formatIsoDate(s.publishedOn)}` : ""}
        {verificationText(s.verifiedAt, s.verifiedBy)}
      </p>
      <blockquote className="mt-3 max-h-80 overflow-auto whitespace-pre-wrap border-l-2 border-border pl-3 text-sm">{s.excerpt}</blockquote>
      {s.notes && <p className="mt-2 text-xs text-muted-foreground">{s.notes}</p>}
      {s.url && (
        <a href={s.url} target="_blank" rel="noreferrer noopener" className="mt-2 inline-flex items-center gap-1 text-sm text-primary hover:underline">
          Abrir o texto oficial <ExternalLink className="h-3.5 w-3.5" />
        </a>
      )}
    </section>
  );
}
