// ─── Micro-section: the bridge between Pain and Core ─────────────────────────
// Short manifesto showing Orken as invisible operational backbone.

const PAIRS = [
  { context: "Enquanto você vende",          result: "o estoque já atualizou."        },
  { context: "Enquanto a cozinha trabalha",   result: "o pedido já chegou na tela."   },
  { context: "Enquanto a obra avança",        result: "a despesa já foi registrada."  },
];

export function LandingBridge() {
  return (
    <section className="bg-orken-navy py-16 px-5 md:px-8">
      <div className="max-w-4xl mx-auto">
        {/* Left-border accent — manifesto visual treatment */}
        <div className="border-l-2 border-orken-indigo/25 pl-6 space-y-5">
          {PAIRS.map(({ context, result }) => (
            <p key={context} className="text-base sm:text-xl leading-relaxed">
              <span className="text-slate-500">{context}, </span>
              <span className="text-white font-semibold">{result}</span>
            </p>
          ))}
        </div>

        {/* Closing line */}
        <p className="mt-10 pl-6 text-sm leading-relaxed max-w-lg">
          <span className="text-slate-500">O Orken acontece no fundo. </span>
          <span className="text-white font-semibold">Para você aparecer na frente.</span>
        </p>
      </div>
    </section>
  );
}
