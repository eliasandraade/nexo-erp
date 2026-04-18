import { Zap, ShieldCheck, Store, RefreshCw } from "lucide-react";

const DIFFS = [
  {
    icon: Zap,
    title: "Tudo integrado de verdade",
    description:
      "PDV, estoque e caixa se comunicam automaticamente. Sem precisar exportar, importar ou sincronizar nada.",
  },
  {
    icon: Store,
    title: "Varejo e restaurante no mesmo sistema",
    description:
      "Não precisa de dois sistemas diferentes. Uma conta, uma equipe, uma visão do negócio.",
  },
  {
    icon: RefreshCw,
    title: "Atualizações sem downtime",
    description:
      "O sistema evolui continuamente sem interromper sua operação. Você nunca perde uma venda por manutenção.",
  },
  {
    icon: ShieldCheck,
    title: "Dados isolados por empresa",
    description:
      "Multi-tenant seguro. Os dados da sua empresa pertencem só a você — nenhum outro acesso.",
  },
];

export function LandingDifferentials() {
  return (
    <section className="bg-orken-graphite py-20 px-5 md:px-8 border-t border-white/5">
      <div className="max-w-4xl mx-auto">

        <p className="text-center text-xs font-bold uppercase tracking-widest text-orken-indigo mb-3">
          Por que o Orken
        </p>
        <h2 className="text-center text-2xl sm:text-3xl font-bold text-white mb-4">
          Diferente por dentro
        </h2>
        <p className="text-center text-slate-400 text-sm max-w-md mx-auto mb-12 leading-relaxed">
          Não é só mais um sistema. É a integração que os outros não têm.
        </p>

        <div className="grid grid-cols-1 sm:grid-cols-2 gap-5">
          {DIFFS.map(({ icon: Icon, title, description }) => (
            <div
              key={title}
              className="flex gap-4 bg-white/4 border border-white/8 rounded-2xl p-6"
            >
              <div className="w-10 h-10 rounded-xl bg-orken-indigo/10 flex items-center justify-center shrink-0">
                <Icon className="h-5 w-5 text-orken-indigo" />
              </div>
              <div>
                <p className="text-white font-semibold text-sm mb-1">{title}</p>
                <p className="text-slate-400 text-sm leading-relaxed">{description}</p>
              </div>
            </div>
          ))}
        </div>

      </div>
    </section>
  );
}
