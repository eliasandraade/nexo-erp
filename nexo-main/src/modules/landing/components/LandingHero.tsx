const SIGNUP_URL = "https://app.orken.com.br/auth/register";

export function LandingHero() {
  return (
    <section className="bg-orken-navy pt-28 pb-24 px-5 md:px-8">
      <div className="max-w-3xl mx-auto text-center">

        {/* Badge */}
        <div className="inline-flex items-center gap-2 bg-orken-indigo/10 border border-orken-indigo/25 text-orken-indigo text-xs font-semibold px-4 py-1.5 rounded-full mb-8 tracking-wide uppercase">
          PDV · Estoque · Caixa · Restaurante
        </div>

        {/* Headline */}
        <h1 className="text-4xl sm:text-5xl md:text-6xl font-extrabold text-white leading-[1.1] tracking-tight mb-6">
          Controle sua operação
          <br />
          <span className="text-orken-indigo">de qualquer lugar.</span>
        </h1>

        {/* Sub */}
        <p className="text-lg text-slate-400 max-w-xl mx-auto leading-relaxed mb-10">
          Varejo e restaurante no mesmo sistema. Cada venda atualiza
          o estoque e o caixa automaticamente — sem planilha, sem
          retrabalho.
        </p>

        {/* CTA */}
        <div className="flex flex-col sm:flex-row items-center justify-center gap-4">
          <a
            href={SIGNUP_URL}
            className="inline-flex items-center gap-2 bg-orken-indigo hover:bg-orken-indigo-dark text-white font-semibold px-8 py-3.5 rounded-xl transition-colors text-base"
          >
            Criar minha conta grátis
            <span className="text-lg leading-none">→</span>
          </a>
          <p className="text-sm text-slate-500">
            Sem cartão · Configuração em minutos
          </p>
        </div>

      </div>
    </section>
  );
}
