const SIGNUP_URL = "https://app.orken.com.br/register";

export function LandingCtaFinal() {
  return (
    <section className="bg-[#07091a] py-28 px-5 md:px-8 border-t border-white/5">
      <div className="max-w-2xl mx-auto">

        {/* Headline — the relief moment */}
        <h2 className="font-display text-[2.25rem] sm:text-[3rem] md:text-[3.5rem] font-extrabold text-white leading-[1.08] tracking-normal mb-6">
          A operação que você
          <br />
          sempre quis ter.
        </h2>

        {/* What disappears — framed as freedom, not features */}
        <p className="text-slate-400 text-lg leading-relaxed max-w-xl mb-3">
          Sem planilha no fim do dia.
          Sem diferença inexplicável no caixa.
          Sem contar estoque na mão antes de fazer pedido.
        </p>
        <p className="text-slate-500 text-base mb-12">
          Só a operação rodando — e você com tempo pra pensar no negócio.
        </p>

        {/* CTA */}
        <div className="flex flex-col sm:flex-row items-start gap-4">
          <a
            href={SIGNUP_URL}
            className="inline-flex items-center gap-2 bg-orken-indigo hover:bg-orken-indigo/90 text-white font-bold px-8 py-4 rounded-xl transition-colors text-base whitespace-nowrap"
          >
            Começar grátis →
          </a>
          <div className="flex flex-col justify-center gap-1 pt-1 sm:pt-2">
            <p className="text-sm text-slate-500">Sem cartão de crédito</p>
            <p className="text-sm text-slate-500">Configuração em minutos</p>
          </div>
        </div>

      </div>
    </section>
  );
}
