import { useEffect, useRef, useState } from "react";
import { Link, useParams } from "react-router-dom";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { AlertCircle, CalendarCheck, CalendarX, Loader2 } from "lucide-react";
import {
  BookingApiError, cancelAppointmentByToken, getAppointmentByToken, type PublicAppointmentView,
} from "../api/booking.api";
import { usePortal } from "../hooks/usePublicBooking";
import { formatSlotLabel } from "../lib/booking-format";
import { getPortalTheme } from "../lib/portal-theme";
import { PortalThemeRoot } from "../components/PortalThemeRoot";
import { Btn, Muted, Reveal, Surface } from "../components/PortalPrimitives";

const STATUS_TEXT: Record<string, string> = {
  Scheduled:  "Agendado — aguardando confirmação",
  Confirmed:  "Confirmado",
  InProgress: "Em atendimento",
  Completed:  "Realizado",
  Cancelled:  "Cancelado",
  NoShow:     "Não compareceu",
};

/** The customer's own page for a booking: see it and cancel it, no login (the link is the key). */
export default function ManageBookingPage() {
  const { slug = "", token = "" } = useParams();
  const qc = useQueryClient();
  const portalQ = usePortal(slug);
  const theme = getPortalTheme(portalQ.data?.presetKey);
  const key = ["svc-portal-booking", slug, token];
  const bookingQ = useQuery({
    queryKey: key, queryFn: () => getAppointmentByToken(slug, token),
    retry: false, refetchOnWindowFocus: false,          // the public rate limit is shared with booking
  });
  const [confirming, setConfirming] = useState(false);
  const confirmRef = useRef<HTMLDivElement>(null);
  useEffect(() => { if (confirming) confirmRef.current?.focus(); }, [confirming]);
  const cancel = useMutation({
    mutationFn: () => cancelAppointmentByToken(slug, token),
    onSuccess: (view) => { qc.setQueryData(key, view); setConfirming(false); },
    // e.g. the deadline passed with the page open: re-read so the screen tells the truth
    onError: () => { setConfirming(false); void qc.invalidateQueries({ queryKey: key }); },
  });

  const wrap = (children: React.ReactNode) => (
    <PortalThemeRoot theme={theme} brandColor={portalQ.data?.brandColor}>
      <div className="flex min-h-screen items-center justify-center px-5 py-10">{children}</div>
    </PortalThemeRoot>
  );

  if (bookingQ.isLoading) {
    return wrap(<span role="status" aria-label="Carregando agendamento"><Loader2 className="h-7 w-7 animate-spin" style={{ color: "var(--p-muted)" }} /></span>);
  }
  if (bookingQ.isError || !bookingQ.data) {
    const notFound = bookingQ.error instanceof BookingApiError && bookingQ.error.status === 404;
    return wrap(
      <div role="alert" className="flex max-w-xs flex-col items-center gap-3 text-center">
        <span style={{ color: "var(--p-muted)" }}><AlertCircle className="h-9 w-9" /></span>
        <p className="text-lg font-bold" style={{ fontFamily: "var(--p-display)" }}>
          {notFound ? "Agendamento não encontrado" : "Não foi possível abrir o agendamento"}
        </p>
        <Muted className="text-sm">
          {notFound ? "O link pode estar incompleto. Confira a mensagem em que você o recebeu." : "Verifique sua conexão e tente de novo em instantes."}
        </Muted>
        {!notFound && <Btn variant="outline" onClick={() => void bookingQ.refetch()}>Tentar de novo</Btn>}
      </div>,
    );
  }

  const b: PublicAppointmentView = bookingQ.data;
  const cancelled = b.status === "Cancelled";
  const deadline = new Date(b.cancelDeadline).toLocaleString("pt-BR", {
    day: "2-digit", month: "2-digit", hour: "2-digit", minute: "2-digit",
  });
  const cancelError = cancel.error instanceof BookingApiError ? cancel.error.message : cancel.error ? "Não foi possível cancelar agora." : null;

  return wrap(
    <Reveal className="w-full max-w-md">
      <p className="text-center text-sm font-semibold">{b.storeName}</p>
      <Surface className="mt-4 p-5">
        <p className="inline-flex items-center gap-1.5 text-xs font-semibold uppercase tracking-wide"
          style={{ color: cancelled ? "var(--p-muted)" : "var(--p-accent)" }}>
          {cancelled ? <CalendarX className="h-4 w-4" /> : <CalendarCheck className="h-4 w-4" />}
          {STATUS_TEXT[b.status] ?? b.status}
        </p>
        <p className="mt-3 text-base font-semibold" style={{ fontFamily: "var(--p-display)" }}>{b.serviceName}</p>
        <p className="mt-1 text-sm">{formatSlotLabel(b.startsAt)}</p>
        <Muted className="mt-1 block text-sm">com {b.professionalName}</Muted>
      </Surface>

      {b.canCancel && !confirming && (
        <div className="mt-5 text-center">
          <Btn variant="outline" onClick={() => setConfirming(true)}>Cancelar agendamento</Btn>
          <Muted className="mt-2 block text-[12px]">Dá para cancelar por aqui até {deadline}.</Muted>
        </div>
      )}
      {b.canCancel && confirming && (
        <Surface className="mt-5 p-4 text-center">
          <div ref={confirmRef} tabIndex={-1} className="outline-none">
            <p className="text-sm font-semibold">Cancelar este agendamento?</p>
          </div>
          <Muted className="mt-1 block text-[12px]">O horário fica livre para outra pessoa.</Muted>
          <div className="mt-3 grid grid-cols-2 gap-2.5">
            <Btn variant="soft" onClick={() => setConfirming(false)} disabled={cancel.isPending}>Manter</Btn>
            <Btn onClick={() => cancel.mutate()} disabled={cancel.isPending}>
              {cancel.isPending ? <Loader2 className="h-4 w-4 animate-spin" /> : "Sim, cancelar"}
            </Btn>
          </div>
        </Surface>
      )}
      {cancelError && <p role="alert" className="mt-3 text-center text-[13px] text-red-600">{cancelError}</p>}
      {!b.canCancel && !cancelled && (b.status === "Scheduled" || b.status === "Confirmed") && (
        <Muted className="mt-5 block text-center text-[13px]">
          {b.handledByStore
            ? "Este agendamento já tem pagamento ou comanda. Para desmarcar, fale com o estabelecimento."
            : `O prazo para cancelar por aqui terminou em ${deadline}. Para desmarcar, fale com o estabelecimento.`}
        </Muted>
      )}
      {cancelled && (
        <div className="mt-5 text-center">
          <Link to={`/agendar/${slug}`} className="text-[13px] font-semibold underline underline-offset-2" style={{ color: "var(--p-accent)" }}>
            Agendar outro horário
          </Link>
        </div>
      )}
    </Reveal>,
  );
}
