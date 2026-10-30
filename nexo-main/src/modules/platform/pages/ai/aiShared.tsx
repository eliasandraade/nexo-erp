import { AlertCircle } from "lucide-react";
import { ApiError } from "@/services/api-client";
import { platformErrorMessage } from "../../services/platformErrors";
import type { AiProviderStatus } from "../../types/aiOperations";

// ── Provider status ───────────────────────────────────────────────────────────

const STATUS_LABEL: Record<AiProviderStatus, string> = {
  available:              "Disponível",
  disabled:               "Desativado",
  blocked_by_deployment:  "Bloqueado na implantação",
  missing_key:            "Sem chave",
  missing_model:          "Sem modelo",
  implementation_missing: "Não disponível nesta versão",
  secret_unavailable:     "Chave ilegível",
  invalid_credentials:    "Chave recusada",
  rate_limited:           "Limitado",
  unhealthy:              "Com falha",
};

const STATUS_STYLE: Record<AiProviderStatus, string> = {
  available:              "bg-green-500/10 text-green-700 border-green-500/30",
  disabled:               "bg-muted text-muted-foreground border-border",
  blocked_by_deployment:  "bg-muted text-muted-foreground border-border",
  missing_key:            "bg-amber-500/10 text-amber-700 border-amber-500/30",
  missing_model:          "bg-amber-500/10 text-amber-700 border-amber-500/30",
  implementation_missing: "bg-muted text-muted-foreground border-border",
  secret_unavailable:     "bg-red-500/10 text-red-700 border-red-500/30",
  invalid_credentials:    "bg-red-500/10 text-red-700 border-red-500/30",
  rate_limited:           "bg-amber-500/10 text-amber-700 border-amber-500/30",
  unhealthy:              "bg-red-500/10 text-red-700 border-red-500/30",
};

export function providerStatusLabel(status: string): string {
  return STATUS_LABEL[status as AiProviderStatus] ?? status;
}

export function ProviderStatusBadge({ status }: Readonly<{ status: string }>) {
  const style = STATUS_STYLE[status as AiProviderStatus] ?? "bg-muted text-muted-foreground border-border";
  return (
    <span className={`inline-flex items-center text-[11px] font-medium px-2 py-0.5 rounded-full border ${style}`}>
      {providerStatusLabel(status)}
    </span>
  );
}

// ── Call / test outcomes ──────────────────────────────────────────────────────

const REASON_LABEL: Record<string, string> = {
  ok:                     "OK",
  invalid_credentials:    "Chave recusada pelo provedor",
  rate_limited:           "Limite de uso do provedor atingido",
  model_not_found:        "Modelo não encontrado",
  timeout:                "Tempo esgotado",
  network:                "Falha de rede",
  provider_error:         "Erro do provedor",
  bad_request:            "Requisição recusada pelo provedor",
  invalid_response:       "Resposta inválida do provedor",
  refused:                "O provedor recusou a solicitação",
  unsupported_input:      "Entrada não suportada",
  missing_key:            "Sem chave",
  missing_model:          "Sem modelo",
  secret_unavailable:     "Chave ilegível",
  implementation_missing: "Não disponível nesta versão",
};

export function reasonLabel(reason: string): string {
  return REASON_LABEL[reason] ?? reason;
}

/** Any thrown value as text for a toast: the backend's own message when there is one. */
export function errorText(e: unknown, fallback = "Falha na comunicação com o backend."): string {
  return platformErrorMessage(e, fallback);
}

const ERROR_TITLE: Record<number, string> = {
  400: "Requisição inválida",
  403: "Sem permissão — exige super administrador com verificação em duas etapas",
  404: "Não encontrado",
  422: "Não foi possível executar",
  503: "O provedor falhou",
};

/** Shows an API error in full: message, code, reason and requestId. Never swallowed. */
export function ApiErrorPanel({ error, title }: Readonly<{ error: unknown; title?: string }>) {
  const api = error instanceof ApiError ? error : null;
  const reason = api?.details?.[0];
  const heading = title ?? (api ? ERROR_TITLE[api.status] : undefined) ?? "Erro";
  return (
    <div role="alert" className="flex items-start gap-3 rounded-lg border border-destructive/30 bg-destructive/5 px-4 py-3">
      <AlertCircle className="h-4 w-4 shrink-0 mt-0.5 text-destructive" />
      <div className="space-y-1 text-sm min-w-0">
        <p className="font-medium text-destructive">{heading}</p>
        <p className="text-foreground break-words">{errorText(error)}</p>
        <dl className="text-xs text-muted-foreground space-y-0.5">
          {api && (
            <div className="flex gap-1.5"><dt>HTTP:</dt><dd className="font-mono">{api.status}</dd></div>
          )}
          {api?.code && (
            <div className="flex gap-1.5"><dt>Código:</dt><dd className="font-mono">{api.code}</dd></div>
          )}
          {reason && (
            <div className="flex gap-1.5">
              <dt>Motivo:</dt>
              <dd>{reasonLabel(reason)} <span className="font-mono">({reason})</span></dd>
            </div>
          )}
          {api?.requestId && (
            <div className="flex gap-1.5"><dt>Request ID:</dt><dd className="font-mono break-all">{api.requestId}</dd></div>
          )}
        </dl>
      </div>
    </div>
  );
}

export function formatUsd(value: number): string {
  if (value === 0) return "US$ 0";
  return `US$ ${value.toLocaleString("pt-BR", { minimumFractionDigits: 2, maximumFractionDigits: 6 })}`;
}

export function formatDateTime(iso: string): string {
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? iso : d.toLocaleString("pt-BR");
}
