import { ShoppingCart, Package, Landmark, UtensilsCrossed, ClipboardList, Bell } from "lucide-react";

const VAREJO_FEATURES = [
  { icon: ShoppingCart,  label: "PDV com leitor de código de barras" },
  { icon: Package,       label: "Controle de estoque em tempo real" },
  { icon: Landmark,      label: "Caixa com abertura, sangria e fechamento" },
];

const RESTAURANTE_FEATURES = [
  { icon: UtensilsCrossed, label: "Mesas, áreas e comandas digitais" },
  { icon: ClipboardList,   label: "Painel de cozinha com status em tempo real" },
  { icon: Bell,            label: "Notificação ao garçom quando o prato está pronto" },
];

function SegmentCard({
  id,
  tag,
  title,
  description,
  features,
}: {
  id: string;
  tag: string;
  title: string;
  description: string;
  features: { icon: React.ElementType; label: string }[];
}) {
  return (
    <div
      id={id}
      className="bg-white/4 border border-white/8 rounded-2xl p-7 space-y-5"
    >
      <span className="inline-block bg-orken-indigo/15 text-orken-indigo text-xs font-bold uppercase tracking-widest px-3 py-1 rounded-full">
        {tag}
      </span>
      <h3 className="text-white text-xl font-bold leading-snug">{title}</h3>
      <p className="text-slate-400 text-sm leading-relaxed">{description}</p>
      <ul className="space-y-3 pt-1">
        {features.map(({ icon: Icon, label }) => (
          <li key={label} className="flex items-center gap-3 text-sm text-slate-300">
            <Icon className="h-4 w-4 text-orken-indigo shrink-0" />
            {label}
          </li>
        ))}
      </ul>
    </div>
  );
}

export function LandingForWhom() {
  return (
    <section className="bg-orken-graphite py-20 px-5 md:px-8 border-t border-white/5">
      <div className="max-w-4xl mx-auto">

        <p className="text-center text-xs font-bold uppercase tracking-widest text-orken-indigo mb-3">
          Para quem é o Orken
        </p>
        <h2 className="text-center text-2xl sm:text-3xl font-bold text-white mb-4">
          Um sistema. Dois mundos.
        </h2>
        <p className="text-center text-slate-400 text-sm max-w-lg mx-auto mb-12 leading-relaxed">
          Varejo e restaurante têm operações muito diferentes. O Orken tem módulos
          específicos para cada um — no mesmo sistema.
        </p>

        <div className="grid grid-cols-1 md:grid-cols-2 gap-5">
          <SegmentCard
            id="varejo"
            tag="Varejo"
            title="Venda no balcão ou loja física"
            description="PDV rápido, estoque integrado e caixa controlado. Do primeiro scan até o fechamento do dia."
            features={VAREJO_FEATURES}
          />
          <SegmentCard
            id="restaurante"
            tag="Restaurante"
            title="Da mesa ao prato, tudo no sistema"
            description="Comanda digital por mesa, painel da cozinha em tempo real e notificação automática ao garçom."
            features={RESTAURANTE_FEATURES}
          />
        </div>

      </div>
    </section>
  );
}
