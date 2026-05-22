const PAINS = [
  {
    number: "01",
    question: "Você sabe exatamente onde foi parar a diferença de R$ 200 no caixa?",
    detail:
      "Sangria não anotada, troco errado, venda sem registro — ao final do dia, a conta não fecha e você não sabe por quê.",
  },
  {
    number: "02",
    question: "Ainda abre planilha depois de fechar o dia para saber se lucrou?",
    detail:
      "Cada venda deveria atualizar o estoque. Mas alguém esqueceu, digitou errado, ou o sistema simplesmente não se comunicou.",
  },
  {
    number: "03",
    question: "Você confia no número de estoque que o sistema mostra — ou sempre faz a contagem manual antes de comprar?",
    detail:
      "Cada venda que não baixa o estoque cria uma divergência. Com o tempo, o número não significa mais nada — e o reabastecimento vira chute.",
  },
];

export function LandingPain() {
  return (
    <section className="bg-orken-graphite py-24 px-5 md:px-8 border-t border-white/5">
      <div className="max-w-4xl mx-auto">

        {/* Section label */}
        <p className="text-xs font-semibold uppercase tracking-widest text-slate-500 mb-4">
          O problema
        </p>

        {/* Headline */}
        <h2 className="font-display text-2xl sm:text-3xl md:text-[2.25rem] font-bold text-white leading-[1.15] tracking-normal mb-16 max-w-2xl">
          O fim do dia não deveria ser o momento mais estressante da operação.
        </h2>

        {/* Numbered items — editorial layout, no cards */}
        <div className="space-y-12">
          {PAINS.map(({ number, question, detail }) => (
            <div
              key={number}
              className="grid grid-cols-[3rem_1fr] sm:grid-cols-[5rem_1fr] gap-4 sm:gap-8 items-start"
            >
              {/* Number */}
              <span className="font-display text-3xl sm:text-4xl font-extrabold text-white/10 leading-none pt-1">
                {number}
              </span>

              {/* Content */}
              <div className="border-t border-white/6 pt-5">
                <p className="text-white font-semibold text-base sm:text-lg leading-snug mb-3">
                  {question}
                </p>
                <p className="text-slate-500 text-sm leading-relaxed">
                  {detail}
                </p>
              </div>
            </div>
          ))}
        </div>

        {/* Resolution — cliffhanger that leads into the Bridge */}
        <div className="mt-16 pt-10 border-t border-white/5">
          <p className="text-slate-400 text-sm leading-relaxed max-w-xl">
            O Orken foi construído para eliminar exatamente isso.{" "}
            <span className="text-white font-medium">
              Não como promessa — como consequência de cada venda,
              cada pedido, cada movimento.
            </span>
          </p>
        </div>

      </div>
    </section>
  );
}
