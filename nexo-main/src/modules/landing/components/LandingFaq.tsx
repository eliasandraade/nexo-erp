import { useState } from "react";
import { ChevronDown } from "lucide-react";
import { cn } from "@/lib/utils";

const FAQS = [
  {
    q: "Preciso de um hardware específico?",
    a: "Não. O Orken funciona no navegador — qualquer computador, tablet ou celular. Para o PDV, recomendamos um leitor de código de barras USB, mas não é obrigatório.",
  },
  {
    q: "Funciona para varejo e restaurante ao mesmo tempo?",
    a: "Sim. Você pode ter módulos de varejo e restaurante ativos na mesma conta. Cada área opera de forma independente, mas aparece no mesmo painel.",
  },
  {
    q: "O sistema funciona offline?",
    a: "O Orken é baseado em nuvem e requer conexão com a internet. Para ambientes com instabilidade de rede, recomendamos uma conexão de backup.",
  },
  {
    q: "Como funciona o suporte?",
    a: "Atendimento por chat e e-mail. Respondemos em até 4 horas úteis. Para planos com múltiplas lojas, há suporte prioritário.",
  },
  {
    q: "Posso importar meus produtos de uma planilha?",
    a: "Sim. O Orken aceita importação em CSV. É o caminho mais rápido para configurar um catálogo grande.",
  },
];

function FaqItem({ q, a }: { q: string; a: string }) {
  const [open, setOpen] = useState(false);

  return (
    <div className="border-b border-white/8 last:border-0">
      <button
        type="button"
        onClick={() => setOpen((v) => !v)}
        className="w-full flex items-center justify-between py-5 text-left gap-4"
      >
        <span className="text-white font-medium text-sm">{q}</span>
        <ChevronDown
          className={cn(
            "h-4 w-4 text-slate-400 shrink-0 transition-transform",
            open && "rotate-180"
          )}
        />
      </button>
      {open && (
        <div className="animate-slide-down">
          <p className="text-slate-400 text-sm leading-relaxed pb-5">{a}</p>
        </div>
      )}
    </div>
  );
}

export function LandingFaq() {
  return (
    <section id="sobre" className="bg-orken-navy py-20 px-5 md:px-8 border-t border-white/5">
      <div className="max-w-2xl mx-auto">

        <p className="text-xs font-semibold uppercase tracking-widest text-orken-indigo mb-4">
          Dúvidas
        </p>
        <h2 className="font-display text-2xl sm:text-3xl font-bold text-white mb-10">
          Perguntas frequentes
        </h2>

        <div>
          {FAQS.map(({ q, a }) => (
            <FaqItem key={q} q={q} a={a} />
          ))}
        </div>

      </div>
    </section>
  );
}
