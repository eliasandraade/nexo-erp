import { useState } from "react";
import { Link } from "react-router-dom";
import { useQuery } from "@tanstack/react-query";
import {
  AlertCircle, AlertTriangle, Check, CheckCircle2, ChevronRight, Clock4, Copy,
  ExternalLink, Loader2, Rocket,
} from "lucide-react";
import { cn } from "@/lib/utils";
import { PageHeader } from "@/components/shared/PageHeader";
import { PageSkeleton } from "@/components/shared/PageSkeleton";
import { Button } from "@/components/ui/button";
import { useAuth } from "@/modules/auth/context/AuthContext";
import { fetchMyStores } from "@/modules/stores/services/storesApi";
import { useServicePreset } from "../context/ServicePresetContext";
import { usePublicBookingSettings } from "../hooks/usePublicBookingSettings";
import { serviceKeys } from "../hooks/useServicePreset";
import { fetchProfessionals, fetchCatalog, type SvcProfessionalDto } from "../api/service.api";
import { professionalHasHours } from "../lib/working-hours";
import {
  buildPortalChecklist, pendingItems, isPortalPublishable,
  type ChecklistItem, type ChecklistStatus, type PortalStatusInput,
} from "../lib/portal-status";
import { ProfessionalHoursDialog } from "../components/ProfessionalHoursDialog";
import { PortalSlugSection, publicBookingUrl } from "../components/PortalSlugSection";
import { PortalBookingSettingsForm } from "../components/PortalBookingSettingsForm";
import { PortalBrandingForm } from "../components/PortalBrandingForm";

export default function ServicePortalPage() {
  const { session } = useAuth();
  const storeId = session?.storeId ?? "";
  const { preset, presetKey, isConfigured } = useServicePreset();

  const storesQ = useQuery({ queryKey: ["stores", "mine"], queryFn: fetchMyStores, enabled: !!storeId });
  const settingsQ = usePublicBookingSettings(!!storeId);
  const prosQ = useQuery({
    queryKey: serviceKeys.professionalsList(true), queryFn: () => fetchProfessionals(true), enabled: !!storeId,
  });
  const catalogQ = useQuery({
    queryKey: serviceKeys.catalogList(true), queryFn: () => fetchCatalog(true), enabled: !!storeId,
  });

  const [editingPro, setEditingPro] = useState<SvcProfessionalDto | null>(null);

  if (storesQ.isLoading || settingsQ.isLoading) return <PageSkeleton />;

  const currentStore = storesQ.data?.find((s) => s.id === storeId) ?? null;
  const pros = prosQ.data ?? [];
  const activeServices = catalogQ.data?.length ?? 0;
  const professionalsWithHours = pros.filter((p) => professionalHasHours(p.workingHoursJson)).length;
  const slug = currentStore?.publicSlug ?? null;

  const statusInput: PortalStatusInput = {
    isConfigured,
    presetDisplayName: preset?.displayName ?? presetKey,
    hasSlug: !!slug,
    bookingEnabled: settingsQ.data?.publicBookingEnabled ?? false,
    activeProfessionals: pros.length,
    professionalsWithHours,
    activeServices,
  };
  const checklist = buildPortalChecklist(statusInput);
  const publishable = isPortalPublishable(statusInput);
  const blockers = pendingItems(checklist);
  const professionalLabel = preset?.labels.professional ?? "Profissional";

  return (
    <div className="mx-auto max-w-2xl space-y-8 p-1">
      <PageHeader
        eyebrow="Orken Service"
        title="Portal público"
        description="Configure o site público onde seus clientes agendam horário."
      />

      {/* 1. Status */}
      <Section title="Status do portal" description="O que já está pronto e o que falta para publicar.">
        <div className="space-y-1.5">
          {checklist.map((item) => <StatusRow key={item.key} item={item} />)}
        </div>
      </Section>

      {/* 2 + 3. Endereço / link público */}
      <Section title="Endereço do portal" description="Seus clientes acessam o agendamento por este link.">
        <PortalSlugSection storeId={storeId} currentSlug={slug} />
      </Section>

      {/* 4. Identidade da loja */}
      <Section title="Identidade da loja" description="Logo, capa, cor, contato e descrição exibidos no portal público.">
        {settingsQ.data?.isConfigured
          ? <PortalBrandingForm settings={settingsQ.data} />
          : <p className="text-sm text-muted-foreground">Escolha o ramo (onboarding) para configurar a identidade.</p>}
      </Section>

      {/* 5. Configurações de agendamento */}
      <Section title="Configurações de agendamento" description="Como o portal oferece horários.">
        {settingsQ.data
          ? <PortalBookingSettingsForm settings={settingsQ.data} />
          : <p className="text-sm text-muted-foreground">Não foi possível carregar as configurações.</p>}
      </Section>

      {/* 5. Horários dos profissionais */}
      <Section
        title="Horários dos profissionais"
        description="A disponibilidade do portal vem dos horários de cada profissional."
      >
        {pros.length === 0 ? (
          <EmptyHint
            text="Nenhum profissional ativo."
            to="/service/profissionais" cta="Cadastrar profissionais"
          />
        ) : (
          <div className="space-y-2">
            {pros.map((p) => {
              const ok = professionalHasHours(p.workingHoursJson);
              return (
                <div key={p.id} className="flex items-center gap-3 rounded-xl border border-border bg-card p-3">
                  <div className="min-w-0 flex-1">
                    <p className="truncate text-sm font-medium">{p.name}</p>
                    <p className={cn("mt-0.5 flex items-center gap-1 text-xs",
                      ok ? "text-primary" : "text-amber-500")}>
                      {ok ? <Check className="h-3 w-3" /> : <Clock4 className="h-3 w-3" />}
                      {ok ? "Horário configurado" : "Sem horário configurado"}
                    </p>
                  </div>
                  <Button variant="outline" size="sm" className="shrink-0" onClick={() => setEditingPro(p)}>
                    Configurar horários
                  </Button>
                </div>
              );
            })}
          </div>
        )}
      </Section>

      {/* 6. Publicação / preview */}
      <Section title="Publicação" description="Verifique tudo antes de divulgar o link.">
        {publishable && slug ? (
          <div className="flex flex-col gap-3 rounded-xl border border-primary/30 bg-primary/5 p-4">
            <div className="flex items-center gap-2 text-sm font-medium text-primary">
              <Rocket className="h-4 w-4" /> Tudo pronto — seu portal está no ar.
            </div>
            <p className="text-sm">
              Seu portal está disponível em <span className="font-medium">{publicBookingUrl(slug)}</span>
            </p>
            <div className="flex gap-2">
              <Button size="sm" asChild>
                <a href={`https://${publicBookingUrl(slug)}`} target="_blank" rel="noopener noreferrer">
                  <ExternalLink className="mr-1.5 h-3.5 w-3.5" /> Abrir em nova aba
                </a>
              </Button>
              <Button size="sm" variant="outline"
                onClick={() => navigator.clipboard.writeText(`https://${publicBookingUrl(slug)}`)}>
                <Copy className="mr-1.5 h-3.5 w-3.5" /> Copiar link
              </Button>
            </div>
          </div>
        ) : (
          <div className="rounded-xl border border-amber-700/30 bg-amber-950/20 p-4">
            <p className="mb-2 flex items-center gap-2 text-sm font-medium text-amber-400">
              <AlertTriangle className="h-4 w-4" /> Antes de publicar, falta:
            </p>
            <ul className="space-y-1">
              {blockers.map((b) => (
                <li key={b.key} className="flex items-start gap-2 text-sm text-muted-foreground">
                  <span className="mt-1.5 h-1 w-1 shrink-0 rounded-full bg-amber-500" />
                  <span>{b.label}{b.hint ? <span className="text-muted-foreground/70"> — {b.hint}</span> : null}</span>
                </li>
              ))}
            </ul>
          </div>
        )}
      </Section>

      <ProfessionalHoursDialog
        professional={editingPro}
        professionalLabel={professionalLabel}
        onClose={() => setEditingPro(null)}
      />
    </div>
  );
}

// ── local helpers ───────────────────────────────────────────────────────────────

function Section({ title, description, children }: {
  title: string; description?: string; children: React.ReactNode;
}) {
  return (
    <section className="space-y-3">
      <div>
        <h2 className="text-sm font-semibold">{title}</h2>
        {description && <p className="mt-0.5 text-xs text-muted-foreground">{description}</p>}
      </div>
      {children}
    </section>
  );
}

const STATUS_ICON: Record<ChecklistStatus, typeof CheckCircle2> = {
  ok: CheckCircle2,
  warn: AlertTriangle,
  pending: AlertCircle,
};
const STATUS_COLOR: Record<ChecklistStatus, string> = {
  ok: "text-primary",
  warn: "text-amber-500",
  pending: "text-muted-foreground",
};

function StatusRow({ item }: { item: ChecklistItem }) {
  const Icon = STATUS_ICON[item.status];
  return (
    <div className="flex items-center gap-3 rounded-lg border border-border bg-card px-3 py-2.5">
      <Icon className={cn("h-4 w-4 shrink-0", STATUS_COLOR[item.status])} />
      <div className="min-w-0 flex-1">
        <p className="text-sm font-medium">{item.label}</p>
        {item.hint && <p className="mt-0.5 truncate text-xs text-muted-foreground">{item.hint}</p>}
      </div>
    </div>
  );
}

function EmptyHint({ text, to, cta }: { text: string; to: string; cta: string }) {
  return (
    <div className="flex items-center justify-between rounded-xl border border-dashed border-border px-4 py-6">
      <p className="text-sm text-muted-foreground">{text}</p>
      <Button variant="outline" size="sm" asChild>
        <Link to={to}>{cta} <ChevronRight className="ml-1 h-3.5 w-3.5" /></Link>
      </Button>
    </div>
  );
}
