const STEPS = [
  {
    number: "01",
    title: "Crie sua conta",
    description:
      "Cadastro em menos de 1 minuto. Sem formulário longo, sem cartão de crédito.",
  },
  {
    number: "02",
    title: "Configure sua operação",
    description:
      "Cadastre produtos, defina áreas (varejo ou restaurante) e convide sua equipe.",
  },
  {
    number: "03",
    title: "Opere com controle real",
    description:
      "PDV, caixa, estoque e comandas funcionando juntos desde o primeiro dia.",
  },
];

export function LandingHowItWorks() {
  return (
    <section
      id="como-funciona-steps"
      className="bg-orken-navy py-20 px-5 md:px-8 border-t border-white/5"
    >
      <div className="max-w-4xl mx-auto">

        <p className="text-center text-xs font-bold uppercase tracking-widest text-orken-indigo mb-3">
          Primeiros passos
        </p>
        <h2 className="text-center text-2xl sm:text-3xl font-bold text-white mb-4">
          Do cadastro à operação em 3 passos
        </h2>
        <p className="text-center text-slate-400 text-sm max-w-lg mx-auto mb-14 leading-relaxed">
          Sem onboarding complicado. Você configura e começa a operar no mesmo dia.
        </p>

        <div className="grid grid-cols-1 sm:grid-cols-3 gap-8">
          {STEPS.map(({ number, title, description }) => (
            <div key={number} className="space-y-3">
              <span className="text-5xl font-extrabold text-orken-indigo/20 leading-none block">
                {number}
              </span>
              <h3 className="text-white font-semibold text-base">{title}</h3>
              <p className="text-slate-400 text-sm leading-relaxed">{description}</p>
            </div>
          ))}
        </div>

      </div>
    </section>
  );
}
