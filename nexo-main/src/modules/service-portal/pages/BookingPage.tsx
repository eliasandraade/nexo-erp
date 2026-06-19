import { useEffect, useRef, useState } from "react";
import { useParams } from "react-router-dom";
import { AlertCircle, ArrowLeft, CalendarOff, Check, Loader2 } from "lucide-react";
import { cn } from "@/lib/utils";
import {
  usePortal, useCatalog, useProfessionals, useCreateAppointment,
} from "../hooks/usePublicBooking";
import { usePortalAvailability } from "../hooks/usePortalAvailability";
import {
  BookingApiError,
  type PublicCatalogItem,
  type PublicAppointmentCreated,
  type CreatePublicAppointmentRequest,
} from "../api/booking.api";
import { getPortalTheme } from "../lib/portal-theme";
import { PortalThemeRoot } from "../components/PortalThemeRoot";
import { PortalHero } from "../components/PortalHero";
import { ServiceGrid } from "../components/ServiceGrid";
import { ProfessionalChooser, ANY_PROFESSIONAL } from "../components/ProfessionalChooser";
import { Scheduler } from "../components/Scheduler";
import { GuestForm, type GuestFormValue } from "../components/GuestForm";
import { SuccessCard } from "../components/SuccessCard";
import { Display, Muted } from "../components/PortalPrimitives";

type Step = "service" | "professional" | "datetime" | "details";
const STEPS: { key: Step; label: string }[] = [
  { key: "service", label: "Serviço" },
  { key: "professional", label: "Profissional" },
  { key: "datetime", label: "Horário" },
  { key: "details", label: "Dados" },
];
const PREV: Record<Step, Step> = { service: "service", professional: "service", datetime: "professional", details: "datetime" };

export default function BookingPage() {
  const { slug = "" } = useParams<{ slug: string }>();
  const flowRef = useRef<HTMLDivElement>(null);

  const [step, setStep]         = useState<Step>("service");
  const [service, setService]   = useState<PublicCatalogItem | null>(null);
  const [choice, setChoice]     = useState<string | null>(null);   // ANY_PROFESSIONAL | proId | null
  const [slotIso, setSlotIso]   = useState<string | null>(null);
  const [created, setCreated]   = useState<PublicAppointmentCreated | null>(null);
  const [localError, setLocalError] = useState<string | null>(null);

  const portalQ = usePortal(slug);
  const portal = portalQ.data;
  const bookingOn = Boolean(portal?.isBookingEnabled);

  const catalogQ = useCatalog(slug, bookingOn);
  const prosQ = useProfessionals(slug, bookingOn && Boolean(service));
  const activePros = prosQ.data ?? [];

  const professionalIds = service
    ? (choice === ANY_PROFESSIONAL ? activePros.map((p) => p.id) : choice ? [choice] : [])
    : [];
  const avail = usePortalAvailability(slug, service?.id ?? null, professionalIds);

  const createMut = useCreateAppointment(slug);
  const theme = getPortalTheme(portal?.presetKey);

  // Scroll to the flow when the step advances (not on first mount).
  const firstRender = useRef(true);
  useEffect(() => {
    if (firstRender.current) { firstRender.current = false; return; }
    flowRef.current?.scrollIntoView({ behavior: "smooth", block: "start" });
  }, [step]);

  // ── Top-level states (themed) ───────────────────────────────────────────────
  if (portalQ.isLoading) {
    return <PortalThemeRoot theme={theme}><Center><Loader2 className="h-7 w-7 animate-spin" style={{ color: "var(--p-muted)" }} /></Center></PortalThemeRoot>;
  }
  if (portalQ.isError || !portal) {
    return <PortalThemeRoot theme={theme}><Notice icon={<AlertCircle className="h-9 w-9" />} title="Página não encontrada"
      text="O link pode estar incorreto ou este estabelecimento não está disponível." /></PortalThemeRoot>;
  }
  if (created) {
    return (
      <PortalThemeRoot theme={theme} brandColor={portal.brandColor}>
        <SuccessCard created={created} storeName={portal.displayName || portal.storeName} onRestart={resetAll} />
      </PortalThemeRoot>
    );
  }
  if (!bookingOn) {
    return <PortalThemeRoot theme={theme} brandColor={portal.brandColor}>
      <Notice icon={<CalendarOff className="h-9 w-9" />} title="Agendamento indisponível"
        text="Este estabelecimento não está aceitando agendamentos online no momento." store={portal.storeName} />
    </PortalThemeRoot>;
  }

  // ── Handlers ────────────────────────────────────────────────────────────────
  function selectService(item: PublicCatalogItem) {
    setService(item); setChoice(null); setSlotIso(null); setStep("professional");
  }
  function selectProfessional(id: string) {
    setChoice(id); setSlotIso(null); setStep("datetime");
  }
  function selectSlot(iso: string) { setSlotIso(iso); setLocalError(null); setStep("details"); }
  function resetAll() {
    setCreated(null); setService(null); setChoice(null); setSlotIso(null);
    setStep("service"); setLocalError(null); createMut.reset();
  }

  function confirm(form: GuestFormValue) {
    if (!service || !slotIso) return;
    const professionalId = choice === ANY_PROFESSIONAL ? avail.ownerOf(slotIso) : choice;
    if (!professionalId) {
      setLocalError("Esse horário acabou de ser preenchido. Escolha outro.");
      return;
    }
    const req: CreatePublicAppointmentRequest = {
      customerName: form.customerName.trim(),
      phone: form.phone.trim(),
      catalogItemId: service.id,
      professionalId,
      startsAt: slotIso,
      email: form.email.trim() || null,
      notes: form.notes.trim() || null,
      subject: service.requiresSubject
        ? { displayName: form.subjectName.trim(), notes: form.subjectNotes.trim() || null }
        : null,
    };
    createMut.mutate(req, { onSuccess: setCreated });
  }

  const errorMessage = localError
    ?? (createMut.isError
      ? (createMut.error instanceof BookingApiError ? createMut.error.message : "Não foi possível concluir o agendamento. Tente novamente.")
      : null);

  const stepIndex = STEPS.findIndex((s) => s.key === step);
  const professionalName = choice === ANY_PROFESSIONAL
    ? "Sem preferência"
    : activePros.find((p) => p.id === choice)?.name ?? "—";

  return (
    <PortalThemeRoot theme={theme} brandColor={portal.brandColor}>
      <PortalHero portal={portal} onStart={() => flowRef.current?.scrollIntoView({ behavior: "smooth" })} />

      <main ref={flowRef} className="mx-auto w-full max-w-3xl scroll-mt-4 px-5 pb-28 pt-9">
        {/* Stepper */}
        <div className="mb-7 flex items-center gap-3">
          {step !== "service" && (
            <button onClick={() => { setLocalError(null); setStep(PREV[step]); }}
              className="inline-flex items-center gap-1 text-[13px] font-medium transition-opacity hover:opacity-70"
              style={{ color: "var(--p-muted)" }}>
              <ArrowLeft className="h-4 w-4" /> Voltar
            </button>
          )}
          <ol className="flex flex-1 items-center gap-1.5">
            {STEPS.map((s, i) => (
              <li key={s.key} className="flex flex-1 items-center gap-1.5">
                <span className={cn("flex h-6 w-6 shrink-0 items-center justify-center rounded-full text-[11px] font-bold")}
                  style={i <= stepIndex
                    ? { background: "var(--p-accent)", color: "var(--p-accent-ink)" }
                    : { background: "var(--p-bg-soft)", color: "var(--p-muted)" }}>
                  {i < stepIndex ? <Check className="h-3.5 w-3.5" /> : i + 1}
                </span>
                <span className="hidden text-[12px] font-medium sm:inline"
                  style={{ color: i <= stepIndex ? "var(--p-ink)" : "var(--p-muted)" }}>{s.label}</span>
                {i < STEPS.length - 1 && <span className="h-px flex-1" style={{ background: "var(--p-line)" }} />}
              </li>
            ))}
          </ol>
        </div>

        <h2 className="mb-4 text-xl font-bold tracking-[-0.01em]">
          <Display>
            {step === "service" && `Escolha o ${portal.labels.catalogItem.toLowerCase()}`}
            {step === "professional" && `Escolha o ${portal.labels.professional.toLowerCase()}`}
            {step === "datetime" && "Escolha data e horário"}
            {step === "details" && "Seus dados"}
          </Display>
        </h2>

        {step === "service" && (
          <ServiceGrid items={catalogQ.data ?? []} showPrices={portal.showPrices}
            labels={portal.labels} selectedId={service?.id ?? null} onSelect={selectService} />
        )}
        {step === "professional" && (
          prosQ.isLoading
            ? <Center className="py-10"><Loader2 className="h-6 w-6 animate-spin" style={{ color: "var(--p-muted)" }} /></Center>
            : <ProfessionalChooser professionals={activePros} labels={portal.labels} selectedId={choice} onSelect={selectProfessional} />
        )}
        {step === "datetime" && (
          <Scheduler slots={avail.slots} loading={avail.isLoading} isError={avail.isError}
            selectedStart={slotIso} onSelect={selectSlot} />
        )}
        {step === "details" && service && slotIso && (
          <GuestForm service={service} professionalName={professionalName} slotIso={slotIso}
            labels={portal.labels} showPrice={portal.showPrices} requiresSubject={service.requiresSubject}
            submitting={createMut.isPending} errorMessage={errorMessage} onConfirm={confirm} />
        )}
      </main>
    </PortalThemeRoot>
  );
}

function Center({ children, className }: { children: React.ReactNode; className?: string }) {
  return <div className={cn("flex min-h-screen items-center justify-center", className)}>{children}</div>;
}

function Notice({ icon, title, text, store }: { icon: React.ReactNode; title: string; text: string; store?: string }) {
  return (
    <div className="flex min-h-screen flex-col items-center justify-center px-6 text-center">
      <div className="flex max-w-xs flex-col items-center gap-3">
        {store && <p className="text-sm font-semibold">{store}</p>}
        <span style={{ color: "var(--p-muted)" }}>{icon}</span>
        <p className="text-lg font-bold" style={{ fontFamily: "var(--p-display)" }}>{title}</p>
        <Muted className="text-sm">{text}</Muted>
      </div>
    </div>
  );
}
