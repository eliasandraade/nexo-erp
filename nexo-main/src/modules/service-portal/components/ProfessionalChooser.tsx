import { Check, Sparkles } from "lucide-react";
import type { PublicProfessional, ServiceLabels } from "../api/booking.api";
import { Surface, Avatar, Reveal, Muted } from "./PortalPrimitives";

export const ANY_PROFESSIONAL = "__any__";

interface ProfessionalChooserProps {
  professionals: PublicProfessional[];
  labels:        ServiceLabels;
  /** ANY_PROFESSIONAL | professional id | null */
  selectedId:    string | null;
  onSelect:      (id: string) => void;
}

export function ProfessionalChooser({ professionals, labels, selectedId, onSelect }: ProfessionalChooserProps) {
  const noun = labels.professional.toLowerCase();

  return (
    <div className="flex flex-col gap-2.5">
      {/* No preference */}
      <Reveal>
        <Choice active={selectedId === ANY_PROFESSIONAL} onClick={() => onSelect(ANY_PROFESSIONAL)}>
          <span className="flex h-11 w-11 shrink-0 items-center justify-center rounded-full"
            style={{ background: "var(--p-accent-soft)", color: "var(--p-accent)" }}>
            <Sparkles className="h-5 w-5" />
          </span>
          <div className="min-w-0 flex-1">
            <p className="text-[15px] font-semibold" style={{ fontFamily: "var(--p-display)" }}>Sem preferência</p>
            <Muted className="text-[13px]">Mostramos os horários de toda a equipe e escolhemos um {noun} livre.</Muted>
          </div>
          {selectedId === ANY_PROFESSIONAL && <Check className="h-5 w-5 shrink-0" style={{ color: "var(--p-accent)" }} />}
        </Choice>
      </Reveal>

      {professionals.map((p, i) => (
        <Reveal key={p.id} delay={60 + i * 50}>
          <Choice active={selectedId === p.id} onClick={() => onSelect(p.id)}>
            <Avatar name={p.name} color={p.color} />
            <div className="min-w-0 flex-1">
              <p className="text-[15px] font-semibold leading-tight">{p.name}</p>
              {(p.specialty || p.role) && <Muted className="text-[13px]">{p.specialty || p.role}</Muted>}
            </div>
            {selectedId === p.id && <Check className="h-5 w-5 shrink-0" style={{ color: "var(--p-accent)" }} />}
          </Choice>
        </Reveal>
      ))}

      {professionals.length === 0 && (
        <Surface className="p-6 text-center"><Muted className="text-sm">Nenhum {noun} disponível.</Muted></Surface>
      )}
    </div>
  );
}

function Choice({ active, onClick, children }: { active: boolean; onClick: () => void; children: React.ReactNode }) {
  return (
    <button onClick={onClick} className="block w-full text-left">
      <Surface interactive className="flex items-center gap-3 p-3.5"
        style={active ? { borderColor: "var(--p-accent)" } : undefined}>
        {children}
      </Surface>
    </button>
  );
}
