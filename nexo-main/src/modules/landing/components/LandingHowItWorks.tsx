const STEPS = [
  {
    number: "01",
    title: "Escolha seus módulos",
    detail: "Core para todos. Orken Menu se você tem restaurante. Orken Build se você tem obra.",
  },
  {
    number: "02",
    title: "Configure sua operação",
    detail: "Cadastre produtos e equipe. Em minutos, não em dias. Importação por CSV disponível.",
  },
  {
    number: "03",
    title: "Opera. O Orken cuida do resto.",
    detail:
      "Cada venda baixa o estoque. Cada movimento registra o caixa. Cada pedido chega à cozinha. Automático.",
  },
];

export function LandingHowItWorks() {
  return (
    <section className="bg-orken-graphite py-28 px-5 md:px-8 border-t border-white/5">
      <div className="max-w-4xl mx-auto">

        {/* Header */}
        <div className="mb-16">
          <p className="text-xs font-semibold uppercase tracking-widest text-slate-500 mb-5">
            Como funciona
          </p>
          <h2 className="font-display text-[2rem] sm:text-[2.5rem] md:text-[2.75rem] font-extrabold text-white leading-[1.1] tracking-normal max-w-lg">
            Três minutos para começar.
            <br />
            <span className="text-slate-400 font-bold">Uma operação para controlar.</span>
          </h2>
        </div>

        {/* Steps — no cards, pure typographic hierarchy */}
        <div className="space-y-0">
          {STEPS.map(({ number, title, detail }, i) => (
            <div
              key={number}
              className={`grid grid-cols-[4rem_1fr] sm:grid-cols-[6rem_1fr] gap-6 sm:gap-10 items-start
                ${i < STEPS.length - 1 ? "border-b border-white/5 pb-10 mb-10" : ""}`}
            >
              {/* Number */}
              <span className="font-display text-4xl sm:text-5xl font-extrabold text-white/8 leading-none tabular-nums pt-1">
                {number}
              </span>

              {/* Content */}
              <div className="pt-1.5">
                <h3 className="font-display text-xl sm:text-2xl font-bold text-white leading-tight mb-3">
                  {title}
                </h3>
                <p className="text-slate-500 text-base leading-relaxed max-w-lg">
                  {detail}
                </p>
              </div>
            </div>
          ))}
        </div>
      </div>
    </section>
  );
}
