import type { PublicServicePortal } from "../api/booking.api";
import { Btn } from "./PortalPrimitives";
export function PortalHero({ portal, onStart }: { portal: PublicServicePortal; onStart: () => void }) {
  const name = portal.displayName || portal.storeName;
  return <header className="border-b" style={{background: 'var(--p-surface)', borderColor:'var(--p-line)'}}>
    <div className="mx-auto max-w-3xl px-5 py-8 sm:py-12">
      <div className="flex items-start gap-4">
        {portal.logoUrl && <img src={portal.logoUrl} alt="" className="h-14 w-14 rounded-md object-cover" />}
        <div className="min-w-0"><p className="text-sm" style={{color:'var(--p-muted)'}}>{portal.presetDisplayName}</p><h1 className="mt-1 break-words text-3xl font-semibold tracking-tight">{name}</h1></div>
      </div>
      {portal.coverImageUrl && <img src={portal.coverImageUrl} alt="" className="mt-6 max-h-52 w-full rounded-md object-cover" />}
      <p className="mt-5 max-w-prose text-base leading-relaxed" style={{color:'var(--p-muted)'}}>{portal.description || 'Escolha o serviço, o profissional e um horário disponível.'}</p>
      <Btn onClick={onStart} className="mt-6">Escolher serviço</Btn>
    </div>
  </header>;
}
