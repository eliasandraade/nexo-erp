// ─── Orken Build: operational cost control for construction, without spreadsheets

function SmartEntryDemo() {
  return (
    <div className="w-full max-w-[420px] mx-auto lg:mx-0 space-y-3 select-none pointer-events-none">

      {/* Input moment */}
      <div className="rounded-2xl border border-white/8 bg-[#0d1525] overflow-hidden">
        <div className="px-4 py-3 border-b border-white/5 bg-[#0a1020] flex items-center gap-2">
          <span className="font-display text-sm font-bold text-white">
            Ork<span className="text-orken-indigo">en</span>
          </span>
          <span className="text-white/15 mx-1 text-xs">|</span>
          <span className="text-[11px] text-slate-500">Build · Registrar despesa</span>
        </div>

        <div className="p-4 space-y-3">
          {/* The natural language input */}
          <div>
            <p className="text-[9px] text-slate-600 uppercase tracking-wider mb-1.5">O que aconteceu?</p>
            <div className="bg-white/5 border border-white/10 rounded-xl px-4 py-3">
              <p className="text-sm text-slate-300 leading-relaxed">
                Paguei R$ 240 de cimento na Leroy pra obra da Rua XV
              </p>
            </div>
          </div>

          {/* Interpretation arrow */}
          <div className="flex items-center gap-3">
            <div className="flex-1 h-px bg-white/5" />
            <p className="text-[9px] text-slate-700 shrink-0">interpretado como</p>
            <div className="flex-1 h-px bg-white/5" />
          </div>

          {/* Parsed result */}
          <div className="bg-white/3 border border-white/6 rounded-xl p-4 space-y-2.5">
            {[
              { field: "Categoria",  value: "Materiais",   color: "text-white" },
              { field: "Obra",       value: "Rua XV",      color: "text-orken-indigo" },
              { field: "Fornecedor", value: "Leroy Merlin", color: "text-white" },
              { field: "Valor",      value: "R$ 240,00",   color: "text-emerald-400" },
            ].map(({ field, value, color }) => (
              <div key={field} className="flex items-center justify-between">
                <span className="text-[10px] text-slate-600">{field}</span>
                <span className={`text-[11px] font-semibold ${color}`}>{value}</span>
              </div>
            ))}
          </div>

          {/* Confirm button */}
          <div className="bg-orken-indigo rounded-xl py-2.5 text-center">
            <span className="text-[11px] font-semibold text-white">Confirmar lançamento →</span>
          </div>
        </div>
      </div>

      {/* Result — appears in week's summary */}
      <div className="border border-white/6 rounded-2xl px-5 py-4 bg-white/[0.02]">
        <div className="flex items-center justify-between mb-3">
          <p className="text-[10px] font-semibold text-slate-500 uppercase tracking-wider">
            Obra Rua XV · Esta semana
          </p>
          <span className="text-[9px] text-slate-700">3 lançamentos</span>
        </div>
        <div className="space-y-2">
          {[
            { label: "Materiais",   value: "R$ 620,00" },
            { label: "Mão de obra", value: "R$ 1.400,00" },
            { label: "Aluguel eq.", value: "R$ 180,00" },
          ].map(({ label, value }) => (
            <div key={label} className="flex items-center justify-between">
              <span className="text-[10px] text-slate-400">{label}</span>
              <span className="text-[10px] font-medium text-white">{value}</span>
            </div>
          ))}
          <div className="pt-2 border-t border-white/5 flex items-center justify-between">
            <span className="text-[10px] font-semibold text-slate-400">Total</span>
            <span className="text-sm font-bold text-white">R$ 2.200,00</span>
          </div>
        </div>
      </div>
    </div>
  );
}

export function LandingBuild() {
  return (
    <section
      id="orken-build"
      className="bg-orken-navy py-24 px-5 md:px-8 border-t border-white/5"
    >
      <div className="max-w-6xl mx-auto">
        <div className="grid grid-cols-1 lg:grid-cols-[1fr_440px] gap-14 xl:gap-24 items-center">

          {/* Copy */}
          <div>
            <p className="text-xs font-semibold uppercase tracking-widest text-orken-indigo mb-5">
              Orken Build
            </p>
            <h2 className="font-display text-[2rem] sm:text-[2.5rem] md:text-[2.75rem] font-extrabold text-white leading-[1.1] tracking-normal mb-6">
              Cada real gasto,
              <br />
              <span className="text-orken-indigo">no contexto certo.</span>
            </h2>
            <p className="text-slate-400 text-base leading-relaxed max-w-lg mb-8">
              Controle de despesas por obra, sem planilha.
              Você descreve o que aconteceu — o Orken categoriza, associa
              à obra e lança no relatório da semana.
            </p>

            {/* Operational questions as outcomes — editorial, no card */}
            <div className="space-y-0 border-t border-white/5 pt-8">
              {[
                {
                  question: "Qual obra está consumindo mais?",
                  answer:   "Abra o painel. Já está lá, por categoria.",
                },
                {
                  question: "Onde foi o dinheiro desta semana?",
                  answer:   "Cada lançamento já está atribuído a uma obra.",
                },
                {
                  question: "Quanto gastei em materiais no mês?",
                  answer:   "Filtro pronto. Sem fórmula de Excel.",
                },
              ].map(({ question, answer }, i) => (
                <div
                  key={question}
                  className={`py-5 ${i < 2 ? "border-b border-white/5" : ""}`}
                >
                  <p className="text-white font-semibold text-sm mb-1">{question}</p>
                  <p className="text-slate-500 text-sm leading-relaxed">{answer}</p>
                </div>
              ))}
            </div>
          </div>

          {/* Visual */}
          <SmartEntryDemo />
        </div>
      </div>
    </section>
  );
}
