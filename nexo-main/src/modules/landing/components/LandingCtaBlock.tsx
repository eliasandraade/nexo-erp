const SIGNUP_URL = "https://app.orken.com.br/register";

export function LandingCtaBlock() {
  return (
    <section className="bg-orken-indigo py-16 px-5 md:px-8">
      <div className="max-w-2xl mx-auto text-center space-y-5">
        <h2 className="text-2xl sm:text-3xl font-bold text-white leading-snug">
          Pronto para sair da planilha?
        </h2>
        <p className="text-indigo-200 text-sm leading-relaxed">
          Crie sua conta agora e comece a operar com controle real ainda hoje.
        </p>
        <a
          href={SIGNUP_URL}
          className="inline-flex items-center gap-2 bg-white text-orken-indigo font-bold px-8 py-3.5 rounded-xl hover:bg-orken-indigo-soft transition-colors text-sm"
        >
          Criar minha conta grátis →
        </a>
        <p className="text-indigo-300/70 text-xs">
          Sem cartão de crédito · Sem contrato
        </p>
      </div>
    </section>
  );
}
