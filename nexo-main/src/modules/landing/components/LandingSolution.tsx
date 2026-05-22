// ─── Core section: "Everything talks. Without you having to ask." ─────────────

function TransactionRipple() {
  return (
    <div className="w-full max-w-[400px] mx-auto lg:mx-0 space-y-3">
      {/* The triggering event */}
      <div className="bg-orken-indigo/10 border border-orken-indigo/20 rounded-2xl px-5 py-4">
        <p className="text-[10px] font-semibold uppercase tracking-widest text-orken-indigo mb-2">
          Venda confirmada · 14:32
        </p>
        <p className="text-white font-semibold text-sm mb-0.5">Arroz 5kg × 2</p>
        <p className="font-display text-2xl font-bold text-white tracking-normal">R$ 42,00</p>
      </div>

      {/* Connector */}
      <div className="flex items-center gap-3 px-2">
        <div className="flex-1 h-px bg-white/6" />
        <p className="text-[10px] text-slate-500 shrink-0">disparou automaticamente</p>
        <div className="flex-1 h-px bg-white/6" />
      </div>

      {/* Ripple results */}
      <div className="border border-white/6 rounded-2xl overflow-hidden divide-y divide-white/5">
        {[
          { dot: "bg-emerald-400", label: "Estoque atualizado",  value: "Arroz 5kg: 18 → 16 un." },
          { dot: "bg-orken-indigo", label: "Caixa registrado",   value: "+R$ 42,00" },
          { dot: "bg-amber-400",    label: "Histórico atualizado", value: "1 venda · hoje" },
        ].map(({ dot, label, value }) => (
          <div key={label} className="flex items-center gap-4 px-5 py-3.5 bg-white/[0.02]">
            <div className={`w-1.5 h-1.5 rounded-full shrink-0 ${dot}`} />
            <span className="text-sm text-slate-400 flex-1">{label}</span>
            <span className="text-sm text-white font-medium">{value}</span>
          </div>
        ))}
      </div>

      <p className="text-xs text-slate-500 text-right px-1">
        Sem exportação. Sem digitação extra.
      </p>
    </div>
  );
}

export function LandingCore() {
  return (
    <section
      id="como-funciona"
      className="bg-orken-navy py-24 px-5 md:px-8 border-t border-white/5"
    >
      <div className="max-w-6xl mx-auto">
        <div className="grid grid-cols-1 lg:grid-cols-[1fr_420px] gap-16 xl:gap-24 items-center">

          {/* Copy */}
          <div>
            <p className="text-xs font-semibold uppercase tracking-widest text-orken-indigo mb-5">
              Core
            </p>
            <h2 className="font-display text-[2rem] sm:text-[2.5rem] md:text-[3rem] font-bold text-white leading-[1.1] tracking-normal mb-6">
              Tudo conversa.
              <br />
              <span className="text-orken-indigo">Sem você precisar pedir.</span>
            </h2>
            <p className="text-slate-400 text-base leading-relaxed max-w-lg mb-10">
              PDV, estoque e caixa não são módulos que se integram.
              São o mesmo sistema vendo a mesma operação — ao mesmo tempo.
              Cada venda atualiza tudo. Automático.
            </p>

            {/* Integration as cause → effect — not a feature list */}
            <div className="space-y-6 border-t border-white/5 pt-8">
              {[
                { cause: "Você bate a venda.",          effect: "Estoque já baixou."               },
                { cause: "O dia fecha.",                effect: "O caixa já está conferido."        },
                { cause: "Você quer saber o resultado.", effect: "O relatório já está pronto."      },
              ].map(({ cause, effect }) => (
                <div key={cause} className="space-y-0.5">
                  <p className="text-sm text-slate-500">{cause}</p>
                  <p className="text-sm font-semibold text-white">{effect}</p>
                </div>
              ))}
            </div>
          </div>

          {/* Visual */}
          <TransactionRipple />
        </div>
      </div>
    </section>
  );
}
