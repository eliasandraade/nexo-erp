import { useState } from "react";
import { Link } from "react-router-dom";
import { useMutation } from "@tanstack/react-query";
import { AlertTriangle, CheckCircle2, ChevronDown, ShieldCheck, XCircle } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Collapsible, CollapsibleContent, CollapsibleTrigger } from "@/components/ui/collapsible";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { formatCurrency } from "@/lib/formatters";
import { cn } from "@/lib/utils";
import { verifyTaxCalculation, type EngineInput, type EngineOutput, type TaxVerificationDto } from "../api/tax.api";
import { verificationHeadline } from "../lib/labels";
import { BASE_COMPONENTS, CONDITION_OUTCOMES, RULE_STATUS, TAX_NAMES, labelOf, toneOf } from "../lib/labels";
import { errorMessage, formatPercent } from "../lib/forms";
import { ToneBadge } from "./TaxBadges";

/** Legal source codes as links to their literal text on the rules page. */
export function SourceLinks({ codes }: Readonly<{ codes: string[] }>) {
  if (codes.length === 0) return <span className="text-muted-foreground">sem fonte</span>;
  return (
    <span className="inline-flex flex-wrap gap-1">
      {codes.map((c) => (
        <Link key={c} to={`/tributos/regras?fonte=${encodeURIComponent(c)}`} className="rounded border border-border px-1 font-mono text-[11px] hover:bg-muted">
          {c}
        </Link>
      ))}
    </span>
  );
}

/** "Quanto custa? O que aconteceu? Qual a fonte?" for an engine output, in business words. */
export function EngineResult({ output, input }: Readonly<{ output: EngineOutput; input?: EngineInput }>) {
  const gross = output.lines.reduce((s, l) => s + l.gross, 0);
  const rate = gross > 0 ? (output.totalTax / gross) * 100 : null;
  const hypothesis = output.lines.some((l) => l.components.some((c) => c.isHypothesis));

  return (
    <div className="space-y-4">
      <div className="grid gap-3 sm:grid-cols-3">
        <Figure label="Quanto custa" value={formatCurrency(output.totalTax)} caption="CBS + IBS desta operação" />
        <Figure label="Base de cálculo" value={formatCurrency(output.totalBase)} caption={`de ${formatCurrency(gross)} em valor das linhas`} />
        <Figure label="Peso sobre o valor" value={formatPercent(rate)} caption="imposto ÷ valor das linhas" />
      </div>

      {hypothesis && (
        <p className="rounded-md border border-warning/40 bg-warning/5 p-2 text-sm text-warning">
          Contém alíquota hipotética (simulação): não é a alíquota em vigor.
        </p>
      )}

      {output.totals.length > 0 && (
        <div className="overflow-x-auto rounded-md border border-border">
          <Table>
            <TableHeader>
              <TableRow><TableHead>Tributo</TableHead><TableHead className="text-right">Base</TableHead><TableHead className="text-right">Valor</TableHead></TableRow>
            </TableHeader>
            <TableBody>
              {output.totals.map((t) => (
                <TableRow key={t.tax}>
                  <TableCell>{labelOf(TAX_NAMES, t.tax)}</TableCell>
                  <TableCell className="text-right tabular-nums">{formatCurrency(t.base)}</TableCell>
                  <TableCell className="text-right tabular-nums">{formatCurrency(t.amount)}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </div>
      )}

      {output.warnings.length > 0 && (
        <div className="space-y-1 rounded-md border border-warning/40 bg-warning/5 p-3 text-sm">
          <p className="flex items-center gap-2 font-medium text-warning"><AlertTriangle className="h-4 w-4" /> Atenção</p>
          <ul className="list-disc pl-5 text-foreground">{output.warnings.map((w) => <li key={w}>{w}</li>)}</ul>
        </div>
      )}

      <div className="space-y-2">
        <h4 className="text-sm font-semibold">O que aconteceu em cada linha</h4>
        {output.lines.map((l) => {
          const line = input?.lines.find((x) => x.index === l.index);
          return (
            <div key={l.index} className="rounded-md border border-border p-3 text-sm">
              <p className="font-medium">
                Linha {l.index}: {formatCurrency(l.gross)} → base {formatCurrency(l.base)}
                {line && !line.excludedTaxesInformed && <span className="ml-2 text-xs font-normal text-warning">(ICMS/ISS/PIS/Cofins não informados)</span>}
              </p>
              {l.baseSteps.length > 0 && (
                <ul className="mt-1 space-y-0.5 text-muted-foreground">
                  {l.baseSteps.map((s) => (
                    <li key={`${s.ruleKey}-${s.component}`}>
                      {labelOf(BASE_COMPONENTS, s.component)}: {s.included ? "entra na base" : "sai da base"} ({s.delta >= 0 ? "+" : ""}{formatCurrency(s.delta)})
                    </li>
                  ))}
                </ul>
              )}
              <ul className="mt-1 space-y-0.5">
                {l.components.map((c) => (
                  <li key={`${c.tax}-${c.ruleKey}`} className="flex flex-wrap items-center gap-x-2">
                    <span>{labelOf(TAX_NAMES, c.tax)}: {formatPercent(c.ratePercent, 4)} sobre {formatCurrency(c.base)} = <strong className="tabular-nums">{formatCurrency(c.amount)}</strong></span>
                    {c.isHypothesis && <ToneBadge label="hipótese" tone="warning" />}
                  </li>
                ))}
                {l.components.length === 0 && <li className="text-muted-foreground">Nenhuma alíquota se aplicou a esta linha.</li>}
              </ul>
            </div>
          );
        })}
      </div>

      {output.notes.length > 0 && (
        <div className="space-y-2">
          <h4 className="text-sm font-semibold">Por quê — e qual a fonte</h4>
          <ul className="space-y-2 text-sm">
            {output.notes.map((n) => (
              <li key={`${n.code}-${n.ruleKey}`} className="rounded-md bg-muted/40 p-2">
                <p>{n.text}</p>
                <p className="mt-1 text-xs text-muted-foreground">Fonte: <SourceLinks codes={n.legalSourceCodes} /></p>
              </li>
            ))}
          </ul>
        </div>
      )}

      {output.notApplied.length > 0 && (
        <div className="space-y-1 text-sm">
          <h4 className="font-semibold">Regras que existem mas não foram aplicadas</h4>
          <ul className="space-y-1">
            {output.notApplied.map((r) => (
              <li key={r.ruleVersionId} className="flex flex-wrap items-center gap-2">
                <code className="text-xs">{r.ruleKey}@{r.ruleVersion}</code>
                <ToneBadge label={labelOf(RULE_STATUS, r.status)} tone={toneOf(RULE_STATUS, r.status)} />
              </li>
            ))}
          </ul>
        </div>
      )}
    </div>
  );
}

function Figure({ label, value, caption }: Readonly<{ label: string; value: string; caption?: string }>) {
  return (
    <div className="rounded-md border border-border bg-card p-3">
      <p className="text-xs font-medium text-muted-foreground">{label}</p>
      <p className="mt-1 text-xl font-semibold tabular-nums">{value}</p>
      {caption && <p className="mt-1 text-[11px] text-muted-foreground">{caption}</p>}
    </div>
  );
}

export interface TrailHashes {
  calculationId: string;
  engineVersion: string;
  inputHash: string;
  ruleSetHash: string;
  outputHash: string;
  profileVersionNumber: number;
}

/** "Rastro técnico": every rule run with its conditions, hashes, and the reproduction check. */
export function TechnicalTrail({ output, hashes }: Readonly<{ output: EngineOutput; hashes?: TrailHashes }>) {
  const [open, setOpen] = useState(false);
  const [verification, setVerification] = useState<TaxVerificationDto | null>(null);
  const verify = useMutation({
    mutationFn: () => verifyTaxCalculation(hashes!.calculationId),
    onSuccess: (v) => setVerification(v),
    onError: (e) => toast.error(errorMessage(e)),
  });

  return (
    <Collapsible open={open} onOpenChange={setOpen} className="rounded-md border border-border">
      <CollapsibleTrigger asChild>
        <button type="button" className="flex w-full items-center justify-between gap-2 px-3 py-2 text-left text-sm font-medium">
          Rastro técnico
          <ChevronDown className={cn("h-4 w-4 transition-transform", open && "rotate-180")} />
        </button>
      </CollapsibleTrigger>
      <CollapsibleContent className="space-y-4 border-t border-border p-3 text-sm">
        <dl className="grid gap-x-4 gap-y-1 text-xs sm:grid-cols-[auto_1fr]">
          <dt className="text-muted-foreground">Versão do motor</dt><dd className="font-mono">{hashes?.engineVersion ?? output.engineVersion}</dd>
          {hashes && <>
            <dt className="text-muted-foreground">Versão do perfil</dt><dd className="font-mono">v{hashes.profileVersionNumber}</dd>
            <dt className="text-muted-foreground">Hash da entrada</dt><dd className="break-all font-mono">{hashes.inputHash}</dd>
            <dt className="text-muted-foreground">Hash das regras</dt><dd className="break-all font-mono">{hashes.ruleSetHash}</dd>
            <dt className="text-muted-foreground">Hash do resultado</dt><dd className="break-all font-mono">{hashes.outputHash}</dd>
          </>}
        </dl>

        {hashes && (
          <div className="space-y-2">
            <Button type="button" variant="outline" size="sm" disabled={verify.isPending} onClick={() => verify.mutate()}>
              <ShieldCheck className="mr-1.5 h-3.5 w-3.5" /> {verify.isPending ? "Verificando…" : "Verificar reprodução"}
            </Button>
            {verification && (
              <output className={cn("block rounded-md border p-2 text-xs", verification.reproducible ? "border-success/40 text-success" : "border-destructive/40 text-destructive")}>
                <span className="block font-medium">
                  {verificationHeadline(verification)}
                </span>
                {!verification.engineVersionMatches && (
                  <span className="block mt-1">Motor guardado {verification.storedEngineVersion}, motor atual {verification.currentEngineVersion}.</span>
                )}
                {verification.recomputedOutputHash && (
                  <span className="block mt-1 break-all font-mono text-muted-foreground">recalculado: {verification.recomputedOutputHash}</span>
                )}
                {verification.differences.length > 0 && <ul className="mt-1 list-disc pl-4">{verification.differences.map((d) => <li key={d}>{d}</li>)}</ul>}
              </output>
            )}
          </div>
        )}

        <div className="space-y-2">
          <p className="text-xs font-semibold uppercase tracking-wide text-muted-foreground">Regras executadas ({output.executions.length})</p>
          {output.executions.map((x) => (
            <div key={`${x.ruleVersionId}-${x.lineIndex ?? "op"}`} className="rounded-md border border-border p-2 text-xs">
              <div className="flex flex-wrap items-center gap-2">
                {x.matched ? <CheckCircle2 className="h-3.5 w-3.5 text-success" /> : <XCircle className="h-3.5 w-3.5 text-muted-foreground" />}
                <code className="font-semibold">{x.ruleKey}@{x.ruleVersion}</code>
                {x.lineIndex !== null && <span className="text-muted-foreground">linha {x.lineIndex}</span>}
                <span className={x.matched ? "text-success" : "text-muted-foreground"}>{x.matched ? "aplicada" : "não aplicada"}</span>
                {x.isHypothesis && <ToneBadge label="hipótese" tone="warning" />}
              </div>
              {x.conditions.length > 0 && (
                <ul className="mt-1 space-y-0.5">
                  {x.conditions.map((c, i) => (
                    <li key={`${c.kind}-${i}`} className="flex flex-wrap items-center gap-x-2">
                      <ToneBadge label={labelOf(CONDITION_OUTCOMES, c.outcome)} tone={toneOf(CONDITION_OUTCOMES, c.outcome)} />
                      <span className="font-mono">{c.kind}</span>
                      <span className="text-muted-foreground">esperado {c.expected} · encontrado {c.actual}</span>
                    </li>
                  ))}
                </ul>
              )}
              {x.output && <p className="mt-1 break-words text-muted-foreground">Resultado: {x.output}</p>}
              <p className="mt-1 break-all font-mono text-muted-foreground">conteúdo {x.contentHash}</p>
              <p className="mt-1">Fonte: <SourceLinks codes={x.legalSourceCodes} /></p>
            </div>
          ))}
        </div>
      </CollapsibleContent>
    </Collapsible>
  );
}
