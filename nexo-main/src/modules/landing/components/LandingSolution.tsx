const SOLUTIONS = [
  {
    step: "01",
    title: "Venda registrada",
    description:
      "O PDV captura a venda por código de barras ou busca. Rápido mesmo sob pressão.",
  },
  {
    step: "02",
    title: "Estoque atualizado",
    description:
      "Cada item vendido baixa automaticamente do estoque. Sem planilha, sem digitação extra.",
  },
  {
    step: "03",
    title: "Caixa fechado",
    description:
      "Movimentação registrada na hora. Sangria, suprimento, fechamento — tudo rastreado.",
  },
];

export function LandingSolution() {
  return (
    <section
      id="como-funciona"
      className="bg-orken-navy py-20 px-5 md:px-8 border-t border-white/5"
    >
      <div className="max-w-4xl mx-auto">

        <p className="text-center text-xs font-bold uppercase tracking-widest text-orken-indigo mb-3">
          Como funciona
        </p>
        <h2 className="text-center text-2xl sm:text-3xl font-bold text-white mb-4">
          Uma venda. Três sistemas atualizados.
        </h2>
        <p className="text-center text-slate-400 text-sm max-w-lg mx-auto mb-14 leading-relaxed">
          Quando o caixa confirma a venda, o Orken cuida do resto.
          PDV → Estoque → Caixa. Automático.
        </p>

        <div className="grid grid-cols-1 sm:grid-cols-3 gap-6">
          {SOLUTIONS.map(({ step, title, description }) => (
            <div key={step} className="space-y-3">
              <span className="text-5xl font-extrabold text-orken-indigo/20 leading-none block">
                {step}
              </span>
              <h3 className="text-white font-semibold">{title}</h3>
              <p className="text-slate-400 text-sm leading-relaxed">{description}</p>
            </div>
          ))}
        </div>

      </div>
    </section>
  );
}
