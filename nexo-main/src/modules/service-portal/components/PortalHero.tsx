import { CalendarDays, Clock, ShieldCheck } from "lucide-react";
import type { PublicServicePortal } from "../api/booking.api";
import { Btn, Display, Reveal } from "./PortalPrimitives";

interface PortalHeroProps {
  portal: PublicServicePortal;
  onStart: () => void;
}

export function PortalHero({ portal, onStart }: PortalHeroProps) {
  const name = portal.displayName || portal.storeName;
  const tagline = portal.description
    || "Agende online em poucos toques — escolha o serviço, o profissional e o melhor horário.";

  return (
    <header className="relative overflow-hidden" style={{
      background: "linear-gradient(160deg, var(--p-hero-from), var(--p-hero-to))",
    }}>
      {/* Decorative accent glow — asymmetric, off to the side, never a centered blob. */}
      <div aria-hidden className="pointer-events-none absolute -right-24 -top-24 h-80 w-80 rounded-full blur-3xl"
        style={{ background: "color-mix(in srgb, var(--p-accent) 24%, transparent)" }} />
      <div aria-hidden className="pointer-events-none absolute -left-16 bottom-0 h-48 w-48 rounded-full blur-3xl"
        style={{ background: "color-mix(in srgb, var(--p-accent) 12%, transparent)" }} />

      {portal.coverImageUrl && (
        <img src={portal.coverImageUrl} alt="" aria-hidden
          className="absolute inset-0 h-full w-full object-cover opacity-[0.14]" />
      )}

      <div className="relative mx-auto w-full max-w-3xl px-5 pb-12 pt-14 sm:pt-20">
        <Reveal>
          <div className="flex items-center gap-3">
            {portal.logoUrl ? (
              <img src={portal.logoUrl} alt={name}
                className="h-12 w-12 rounded-2xl object-cover ring-1 ring-black/5" />
            ) : (
              <span className="inline-flex h-11 w-11 items-center justify-center rounded-2xl text-base font-bold"
                style={{ background: "var(--p-accent)", color: "var(--p-accent-ink)", fontFamily: "var(--p-display)" }}>
                {name.charAt(0).toUpperCase()}
              </span>
            )}
            <p className="text-[13px] font-semibold uppercase tracking-[0.18em]" style={{ color: "var(--p-accent)" }}>
              {portal.presetDisplayName}
            </p>
          </div>
        </Reveal>

        <Reveal delay={80}>
          <h1 className="mt-5 text-[clamp(2rem,7vw,3.4rem)] font-bold leading-[1.04] tracking-[-0.02em]">
            <Display>{name}</Display>
          </h1>
        </Reveal>

        <Reveal delay={150}>
          <p className="mt-4 max-w-xl text-[15px] leading-relaxed sm:text-base" style={{ color: "var(--p-muted)" }}>
            {tagline}
          </p>
        </Reveal>

        <Reveal delay={220}>
          <div className="mt-7 flex flex-wrap items-center gap-3">
            <Btn onClick={onStart} className="px-6 py-3 text-[15px]">
              <CalendarDays className="h-[18px] w-[18px]" /> Agendar agora
            </Btn>
            <span className="inline-flex items-center gap-1.5 text-[13px]" style={{ color: "var(--p-muted)" }}>
              <ShieldCheck className="h-4 w-4" style={{ color: "var(--p-accent)" }} /> Confirmação rápida
            </span>
            <span className="inline-flex items-center gap-1.5 text-[13px]" style={{ color: "var(--p-muted)" }}>
              <Clock className="h-4 w-4" style={{ color: "var(--p-accent)" }} /> Sem ligações
            </span>
          </div>
        </Reveal>
      </div>
    </header>
  );
}
