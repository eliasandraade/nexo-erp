import { useState } from "react";
import { useParams } from "react-router-dom";
import { AlertCircle, CalendarOff, Loader2 } from "lucide-react";
import {
  usePortal, useCatalog, useProfessionals, useAvailability, useCreateAppointment,
} from "../hooks/usePublicBooking";
import {
  BookingApiError,
  type PublicCatalogItem,
  type PublicProfessional,
  type PublicAppointmentCreated,
  type CreatePublicAppointmentRequest,
} from "../api/booking.api";
import { BookingShell } from "../components/BookingShell";
import { ServiceStep } from "../components/ServiceStep";
import { ProfessionalStep } from "../components/ProfessionalStep";
import { DateTimeStep } from "../components/DateTimeStep";
import { DetailsStep, type DetailsFormValue } from "../components/DetailsStep";
import { BookingSuccess } from "../components/BookingSuccess";

type Step = "service" | "professional" | "datetime" | "details";
const TOTAL_STEPS = 4;
const STEP_INDEX: Record<Step, number> = { service: 1, professional: 2, datetime: 3, details: 4 };
const PREVIOUS: Record<Step, Step> = {
  service: "service", professional: "service", datetime: "professional", details: "datetime",
};

export default function BookingPage() {
  const { slug = "" } = useParams<{ slug: string }>();

  const [step, setStep]                 = useState<Step>("service");
  const [service, setService]           = useState<PublicCatalogItem | null>(null);
  const [professional, setProfessional] = useState<PublicProfessional | null>(null);
  const [slotIso, setSlotIso]           = useState<string | null>(null);
  const [created, setCreated]           = useState<PublicAppointmentCreated | null>(null);

  const portalQ   = usePortal(slug);
  const portal    = portalQ.data;
  const bookingOn = Boolean(portal?.isBookingEnabled);

  const catalogQ = useCatalog(slug, bookingOn);
  const prosQ    = useProfessionals(slug, bookingOn && Boolean(service));
  const availQ   = useAvailability(slug, service?.id ?? null, professional?.id ?? null);
  const createMut = useCreateAppointment(slug);

  // ── Top-level states ──────────────────────────────────────────────────────
  if (portalQ.isLoading) {
    return (
      <Centered>
        <Loader2 className="h-7 w-7 animate-spin text-muted-foreground" />
      </Centered>
    );
  }

  if (portalQ.isError || !portal) {
    return (
      <Notice
        icon={<AlertCircle className="h-10 w-10 text-muted-foreground" />}
        title="Página não encontrada"
        text="O link pode estar incorreto ou este estabelecimento não está disponível."
      />
    );
  }

  if (created) {
    return <BookingSuccess created={created} onRestart={resetAll} />;
  }

  if (!bookingOn) {
    return (
      <Notice
        storeName={portal.storeName}
        icon={<CalendarOff className="h-10 w-10 text-muted-foreground" />}
        title="Agendamento indisponível"
        text="Este estabelecimento não está aceitando agendamentos online no momento."
      />
    );
  }

  // ── Handlers ──────────────────────────────────────────────────────────────
  function selectService(item: PublicCatalogItem) {
    setService(item);
    setProfessional(null);
    setSlotIso(null);
    setStep("professional");
  }

  function selectProfessional(p: PublicProfessional) {
    setProfessional(p);
    setSlotIso(null);
    setStep("datetime");
  }

  function selectSlot(iso: string) {
    setSlotIso(iso);
    setStep("details");
  }

  function resetAll() {
    setCreated(null);
    setService(null);
    setProfessional(null);
    setSlotIso(null);
    setStep("service");
    createMut.reset();
  }

  function confirm(form: DetailsFormValue) {
    if (!service || !professional || !slotIso) return;
    const req: CreatePublicAppointmentRequest = {
      customerName:   form.customerName.trim(),
      phone:          form.phone.trim(),
      catalogItemId:  service.id,
      professionalId: professional.id,
      startsAt:       slotIso,
      email:          form.email.trim() || null,
      notes:          form.notes.trim() || null,
      subject:        service.requiresSubject
        ? { displayName: form.subjectName.trim(), notes: form.subjectNotes.trim() || null }
        : null,
    };
    createMut.mutate(req, { onSuccess: setCreated });
  }

  const errorMessage = createMut.isError
    ? (createMut.error instanceof BookingApiError
        ? createMut.error.message
        : "Não foi possível concluir o agendamento. Tente novamente.")
    : null;

  return (
    <BookingShell
      storeName={portal.storeName}
      ramo={portal.presetDisplayName}
      stepIndex={STEP_INDEX[step]}
      totalSteps={TOTAL_STEPS}
      canBack={step !== "service"}
      onBack={() => setStep(PREVIOUS[step])}
    >
      {step === "service" && (
        <ServiceStep
          items={catalogQ.data ?? []}
          loading={catalogQ.isLoading}
          showPrices={portal.showPrices}
          labels={portal.labels}
          onSelect={selectService}
        />
      )}

      {step === "professional" && (
        <ProfessionalStep
          items={prosQ.data ?? []}
          loading={prosQ.isLoading}
          labels={portal.labels}
          onSelect={selectProfessional}
        />
      )}

      {step === "datetime" && (
        <DateTimeStep
          availability={availQ.data}
          loading={availQ.isLoading}
          isError={availQ.isError}
          onSelect={selectSlot}
        />
      )}

      {step === "details" && service && professional && slotIso && (
        <DetailsStep
          service={service}
          professional={professional}
          slotIso={slotIso}
          labels={portal.labels}
          showPrices={portal.showPrices}
          submitting={createMut.isPending}
          errorMessage={errorMessage}
          onConfirm={confirm}
        />
      )}
    </BookingShell>
  );
}

// ── Local presentational helpers ──────────────────────────────────────────────

function Centered({ children }: { children: React.ReactNode }) {
  return <div className="flex min-h-screen items-center justify-center bg-background">{children}</div>;
}

function Notice({ icon, title, text, storeName }: {
  icon: React.ReactNode; title: string; text: string; storeName?: string;
}) {
  return (
    <div className="flex min-h-screen flex-col items-center justify-center bg-background px-6 text-center text-foreground">
      <div className="flex max-w-xs flex-col items-center gap-3">
        {storeName && <p className="text-sm font-semibold">{storeName}</p>}
        {icon}
        <p className="font-semibold">{title}</p>
        <p className="text-sm text-muted-foreground">{text}</p>
      </div>
    </div>
  );
}
