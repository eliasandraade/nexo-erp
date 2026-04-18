const PAINS = [
  {
    emoji: "📊",
    headline: "Planilha que ninguém mais entende",
    body: "O estoque vive desatualizado. Quando você percebe a falta, o cliente já foi embora.",
  },
  {
    emoji: "🔌",
    headline: "PDV que não fala com o estoque",
    body: "Cada venda precisa de ajuste manual. Erro humano é só questão de tempo.",
  },
  {
    emoji: "💸",
    headline: "Não sei se estou lucrando",
    body: "Caixa fecha, mas o dinheiro some. Não tem como saber onde está indo.",
  },
];

export function LandingPain() {
  return (
    <section className="bg-orken-graphite py-20 px-5 md:px-8 border-t border-white/5">
      <div className="max-w-4xl mx-auto">

        <p className="text-center text-xs font-bold uppercase tracking-widest text-orken-indigo mb-3">
          Parece familiar?
        </p>
        <h2 className="text-center text-2xl sm:text-3xl font-bold text-white mb-12">
          A realidade de quem opera sem sistema
        </h2>

        <div className="grid grid-cols-1 sm:grid-cols-3 gap-4">
          {PAINS.map(({ emoji, headline, body }) => (
            <div
              key={headline}
              className="bg-white/4 border border-white/8 rounded-2xl p-6 space-y-3"
            >
              <span className="text-3xl">{emoji}</span>
              <p className="font-semibold text-white text-sm leading-snug">{headline}</p>
              <p className="text-slate-400 text-sm leading-relaxed">{body}</p>
            </div>
          ))}
        </div>

        <p className="text-center text-slate-500 text-sm mt-10">
          O Orken foi construído para resolver exatamente isso — de uma vez.
        </p>
      </div>
    </section>
  );
}
