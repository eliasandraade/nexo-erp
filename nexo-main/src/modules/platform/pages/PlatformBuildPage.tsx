import { useState } from "react";
import { toast } from "sonner";
import { AlertTriangle, Database, HardHat, RefreshCw, Sparkles } from "lucide-react";
import { Button } from "@/components/ui/button";
import {
  AlertDialog, AlertDialogAction, AlertDialogCancel, AlertDialogContent, AlertDialogDescription, AlertDialogFooter, AlertDialogHeader,
  AlertDialogTitle,
} from "@/components/ui/alert-dialog";
import { useAdminActionsAllowed } from "../hooks/usePlatformSecurity";
import { usePlatformBuildJobs, usePlatformBuildOverview, useRetryPlatformBuildJob } from "../hooks/usePlatformBuild";
import type { PlatformBuildJob, PlatformBuildJobStatus, PlatformBuildTenant } from "../services/platformApi";
import { platformErrorMessage } from "../services/platformErrors";
import { formatDateTime, formatUsd } from "./ai/aiShared";

/** 1536 → "1,5 KB" (binary units, pt-BR). */
function formatBytes(n: number | null | undefined): string {
  if (n == null) return "—";
  if (n < 1024) return `${n} B`;
  const units = ["KB", "MB", "GB", "TB"];
  let v = n / 1024;
  let i = 0;
  while (v >= 1024 && i < units.length - 1) { v /= 1024; i++; }
  return `${v.toLocaleString("pt-BR", { maximumFractionDigits: 1 })} ${units[i]}`;
}

const usdFromMicros = (micros: number) => formatUsd(micros / 1_000_000);
const int = (n: number) => n.toLocaleString("pt-BR");

const VERSION_STATUS: Record<string, string> = {
  Uploaded: "enviadas", Queued: "na fila", Processing: "processando", Ready: "prontas", Failed: "com falha", Rejected: "recusadas",
};

const JOB_STATUS: Array<{ key: PlatformBuildJobStatus; label: string }> = [
  { key: "Failed", label: "Com falha" },
  { key: "Pending", label: "Pendentes" },
  { key: "Running", label: "Em execução" },
  { key: "Done", label: "Concluídos" },
];

const FLAGS: Array<[key: "build3D" | "buildAi" | "buildVision" | "buildPredict", label: string]> = [
  ["build3D", "3D"], ["buildAi", "IA"], ["buildVision", "Visão"], ["buildPredict", "Previsões"],
];

function Stat({ label, value, alert }: { label: string; value: string; alert?: boolean }) {
  return (
    <div className="min-w-0 rounded-lg border border-border bg-card p-3">
      <p className="truncate text-[11px] text-muted-foreground">{label}</p>
      <p className={`text-lg font-semibold tabular-nums ${alert ? "text-destructive" : "text-foreground"}`}>{value}</p>
    </div>
  );
}

function TenantCard({ t }: { t: PlatformBuildTenant }) {
  const versions = Object.entries(t.modelVersionsByStatus ?? {}).filter(([, n]) => n > 0);
  return (
    <li className="min-w-0 space-y-3 rounded-lg border border-border bg-card p-4" data-testid="platform-build-tenant">
      <div className="flex flex-wrap items-start justify-between gap-2">
        <div className="min-w-0">
          <p className="break-words text-sm font-semibold text-foreground">{t.companyName}</p>
          <p className="text-[11px] text-muted-foreground">Módulo: {t.moduleStatus ?? "sem assinatura (só dados)"}</p>
        </div>
        <ul className="flex flex-wrap gap-1" aria-label="Recursos">
          {FLAGS.map(([k, label]) => (
            <li key={k} className={`rounded-full border px-2 py-0.5 text-[10px] ${t.flags?.[k] ? "border-emerald-500/30 bg-emerald-500/10 text-emerald-700 dark:text-emerald-400" : "border-border text-muted-foreground line-through"}`}>
              {label}
            </li>
          ))}
          {t.flags && !t.flags.customized && <li className="px-1 text-[10px] text-muted-foreground">padrões</li>}
        </ul>
      </div>

      <dl className="grid grid-cols-2 gap-2 text-xs sm:grid-cols-4">
        <div><dt className="text-muted-foreground">Obras ativas / total</dt><dd className="font-medium tabular-nums">{int(t.projectsActive)} / {int(t.projectsTotal)}</dd></div>
        <div><dt className="text-muted-foreground">Portais publicados</dt><dd className="font-medium tabular-nums">{int(t.publishedPortals)}</dd></div>
        <div><dt className="text-muted-foreground">Jobs com falha</dt><dd className={`font-medium tabular-nums ${t.jobsFailed > 0 ? "text-destructive" : ""}`}>{int(t.jobsFailed)}</dd></div>
        <div><dt className="text-muted-foreground">Jobs pendentes</dt><dd className="font-medium tabular-nums">{int(t.jobsPending)}</dd></div>
      </dl>

      <div className="text-xs">
        <p className="text-muted-foreground">Versões de modelo</p>
        <p className="text-foreground">
          {versions.length === 0 ? "Nenhuma" : versions.map(([s, n]) => `${n} ${VERSION_STATUS[s] ?? s}`).join(" · ")}
        </p>
      </div>

      {t.lastJobError && (
        <p className="break-words rounded-md border border-destructive/20 bg-destructive/5 p-2 text-[11px] text-destructive">
          Último erro{t.lastJobErrorAt ? ` (${formatDateTime(t.lastJobErrorAt)})` : ""}: {t.lastJobError}
        </p>
      )}

      <div className="grid gap-3 text-xs sm:grid-cols-2">
        <div className="space-y-0.5" data-testid="platform-build-storage">
          <p className="flex items-center gap-1 font-medium text-foreground"><Database className="h-3.5 w-3.5" aria-hidden /> Armazenamento · {formatBytes(t.storage?.totalBytes)}</p>
          <p className="text-muted-foreground">Modelos {formatBytes(t.storage?.modelsBytes)} · Capturas {formatBytes(t.storage?.realityCaptureBytes)} · Documentos {formatBytes(t.storage?.documentsBytes)}</p>
          <p className="text-muted-foreground">
            Fotos do diário: {int(t.storage?.photosCount ?? 0)}{t.storage?.photosBytes == null ? " (tamanho não registrado)" : ` · ${formatBytes(t.storage.photosBytes)}`}
          </p>
        </div>
        <div className="space-y-0.5">
          <p className="flex items-center gap-1 font-medium text-foreground"><Sparkles className="h-3.5 w-3.5" aria-hidden /> IA nos últimos 30 dias</p>
          <p className="text-muted-foreground">
            {int(t.ai30d?.suggestions ?? 0)} sugestões · {int(t.ai30d?.accepted ?? 0)} aceitas · {int(t.ai30d?.rejected ?? 0)} recusadas
            {t.ai30d?.acceptedRate != null ? ` (${Math.round(t.ai30d.acceptedRate * 100)}% aceitas)` : ""}
          </p>
          <p className="text-muted-foreground">
            {int(t.ai30d?.inputTokens ?? 0)} tokens de entrada · {int(t.ai30d?.outputTokens ?? 0)} de saída · {usdFromMicros(t.ai30d?.costMicros ?? 0)}
          </p>
        </div>
      </div>
    </li>
  );
}

function JobsSection() {
  const [status, setStatus] = useState<PlatformBuildJobStatus>("Failed");
  const { data: jobs = [], isLoading, isError } = usePlatformBuildJobs(status);
  const retry = useRetryPlatformBuildJob();
  const allowed = useAdminActionsAllowed();
  const locked = allowed === false;
  const [confirm, setConfirm] = useState<PlatformBuildJob | null>(null);

  const doRetry = () => {
    if (!confirm) return;
    retry.mutate(confirm.id, {
      onSuccess: () => { toast.success("Job recolocado na fila."); setConfirm(null); },
      onError: (e) => { toast.error(platformErrorMessage(e, "Não foi possível reprocessar.")); setConfirm(null); },
    });
  };

  return (
    <section className="space-y-3 rounded-lg border border-border bg-card p-4" aria-label="Jobs de processamento" data-testid="platform-build-jobs">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h2 className="flex items-center gap-1.5 text-sm font-medium text-foreground"><AlertTriangle className="h-4 w-4 text-amber-600" aria-hidden /> Jobs de processamento de modelos</h2>
        <select aria-label="Situação dos jobs" value={status} onChange={(e) => setStatus(e.target.value as PlatformBuildJobStatus)}
          className="h-8 rounded-md border border-input bg-background px-2 text-xs">
          {JOB_STATUS.map((s) => <option key={s.key} value={s.key}>{s.label}</option>)}
        </select>
      </div>
      {locked && <p className="text-[11px] text-muted-foreground">Reprocessar exige login com verificação em duas etapas.</p>}
      {isLoading && <p className="text-xs text-muted-foreground">Carregando...</p>}
      {isError && <p role="alert" className="text-xs text-destructive">Não foi possível consultar os jobs.</p>}
      {!isLoading && !isError && jobs.length === 0 && <p className="text-xs text-muted-foreground">Nenhum job nesta situação.</p>}
      <ul className="divide-y divide-border">
        {jobs.map((j) => (
          <li key={j.id} className="flex flex-wrap items-start justify-between gap-2 py-2.5" data-testid="platform-build-job">
            <div className="min-w-0 flex-1 space-y-0.5 text-xs">
              <p className="break-words text-sm font-medium text-foreground">{j.companyName}</p>
              <p className="text-muted-foreground">
                Versão {j.versionNumber} · {j.sourceFormat} · {j.kind} · {j.attempts} tentativa(s) · {formatDateTime(j.createdAt)}
                {j.finishedAt ? ` → ${formatDateTime(j.finishedAt)}` : ""}
              </p>
              {j.error && <p className="break-words text-destructive">{j.error}</p>}
            </div>
            {j.status === "Failed" && (
              <Button size="sm" variant="outline" className="h-8 gap-1.5 text-xs" disabled={locked || retry.isPending}
                onClick={() => setConfirm(j)}>
                <RefreshCw className="h-3.5 w-3.5" /> Reprocessar
              </Button>
            )}
          </li>
        ))}
      </ul>

      <AlertDialog open={!!confirm} onOpenChange={(o) => !o && setConfirm(null)}>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Reprocessar este job?</AlertDialogTitle>
            <AlertDialogDescription>
              Um novo job do mesmo tipo é criado para a versão {confirm?.versionNumber} de {confirm?.companyName}, e a versão volta
              para a fila. O job com falha continua no histórico. A ação é auditada.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={retry.isPending}>Cancelar</AlertDialogCancel>
            <AlertDialogAction disabled={retry.isPending} onClick={(e) => { e.preventDefault(); doRetry(); }}>
              Reprocessar
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </section>
  );
}

/** SuperAdmin: Orken Build per company — projects, portals, models, jobs, storage, AI (30 days), flags — and failed jobs. */
export default function PlatformBuildPage() {
  const { data, isLoading, isError } = usePlatformBuildOverview();
  const totals = data?.totals;

  return (
    <div className="max-w-6xl space-y-5 p-4 sm:p-6">
      <div>
        <h1 className="flex items-center gap-2 font-display text-[20px] font-bold tracking-tight text-foreground">
          <HardHat className="h-5 w-5 text-primary" aria-hidden /> Orken Build
        </h1>
        <p className="mt-0.5 text-sm text-muted-foreground">
          Uso do Orken Build por empresa: obras, portais, modelos e jobs, armazenamento por categoria e IA nos últimos 30 dias.
          Nomes de arquivos e chaves de armazenamento nunca aparecem aqui.
        </p>
      </div>

      {isLoading && <p className="text-sm text-muted-foreground">Carregando...</p>}
      {isError && <p role="alert" className="text-sm text-destructive">Não foi possível carregar a visão do Orken Build.</p>}

      {totals && (
        <div className="grid grid-cols-2 gap-2 sm:grid-cols-4" data-testid="platform-build-totals">
          <Stat label="Empresas" value={int(totals.tenants)} />
          <Stat label="Obras ativas / total" value={`${int(totals.projectsActive)} / ${int(totals.projectsTotal)}`} />
          <Stat label="Portais publicados" value={int(totals.publishedPortals)} />
          <Stat label="Jobs com falha" value={int(totals.jobsFailed)} alert={totals.jobsFailed > 0} />
          <Stat label="Jobs pendentes" value={int(totals.jobsPending)} />
          <Stat label="Armazenamento" value={formatBytes(totals.storageBytes)} />
          <Stat label="Sugestões de IA (30 dias)" value={int(totals.aiSuggestions30d)} />
          <Stat label="Custo de IA (30 dias)" value={usdFromMicros(totals.aiCostMicros30d)} />
        </div>
      )}

      {data && (
        data.tenants.length === 0
          ? <p className="text-sm text-muted-foreground">Nenhuma empresa usa o Orken Build.</p>
          : <ul className="grid gap-3 lg:grid-cols-2">{data.tenants.map((t) => <TenantCard key={t.tenantId} t={t} />)}</ul>
      )}
      {data && <p className="text-[11px] text-muted-foreground">Gerado em {formatDateTime(data.generatedAt)}.</p>}

      <JobsSection />
    </div>
  );
}
