const SIGNUP_URL = "https://app.orken.com.br/auth/register";

export function LandingCtaFinal() {
  return (
    <section className="bg-orken-navy py-24 px-5 md:px-8 border-t border-white/5">
      <div className="max-w-xl mx-auto text-center space-y-6">
        <h2 className="text-3xl sm:text-4xl font-extrabold text-white leading-tight tracking-tight">
          Sua operação no controle.
          <br />
          <span className="text-orken-indigo">Hoje mesmo.</span>
        </h2>
        <p className="text-slate-400 text-sm leading-relaxed max-w-sm mx-auto">
          Configure em minutos. Comece a vender com PDV real, estoque e caixa integrados no mesmo dia.
        </p>
        <div className="flex flex-col sm:flex-row items-center justify-center gap-4 pt-2">
          <a
            href={SIGNUP_URL}
            className="inline-flex items-center gap-2 bg-orken-indigo hover:bg-orken-indigo-dark text-white font-bold px-8 py-3.5 rounded-xl transition-colors text-base"
          >
            Criar minha conta grátis →
          </a>
        </div>
        <p className="text-slate-600 text-xs">
          Sem cartão · Sem contrato · Sem surpresa
        </p>
      </div>
    </section>
  );
}
