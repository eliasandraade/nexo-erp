const SIGNUP_URL = "https://app.orken.com.br/register";
const LOGIN_URL  = "https://app.orken.com.br/login";

// ─── PDV mockup — shows the POS in active use, not a generic dashboard ────────

function DashboardMockup() {
  const cartItems = [
    { name: "Arroz 5kg Tio João",  qty: 2, value: "R$ 42,00" },
    { name: "Café 500g Pilão",     qty: 1, value: "R$ 18,90" },
    { name: "Leite UHT Integral",  qty: 4, value: "R$ 31,60" },
  ];

  const quickProducts = [
    { code: "7896", name: "Arroz 5kg",  price: "R$ 21,00" },
    { code: "7134", name: "Café 500g",  price: "R$ 18,90" },
    { code: "2048", name: "Leite UHT",  price: "R$ 7,90"  },
    { code: "5512", name: "Biscoito",   price: "R$ 4,90"  },
    { code: "3301", name: "Detergente", price: "R$ 3,50"  },
    { code: "9104", name: "Sabão pó",   price: "R$ 11,90" },
  ];

  return (
    <div className="relative w-full max-w-[460px] mx-auto lg:mx-0 select-none pointer-events-none">
      {/* Ambient glow */}
      <div className="absolute -inset-8 bg-orken-indigo/6 rounded-full blur-3xl" />

      {/* App frame */}
      <div className="relative rounded-2xl border border-white/10 bg-[#0d1525] overflow-hidden shadow-2xl">

        {/* Topbar */}
        <div className="flex items-center justify-between px-4 py-2.5 border-b border-white/5 bg-[#0a1020]">
          <div className="flex items-center gap-2">
            <span className="font-display text-sm font-bold text-white">
              Ork<span className="text-orken-indigo">en</span>
            </span>
            <span className="text-white/15 text-xs mx-1">|</span>
            <span className="text-[11px] text-slate-500 font-medium">PDV</span>
          </div>
          <div className="flex items-center gap-2">
            <div className="flex items-center gap-1 bg-emerald-500/10 border border-emerald-500/20 rounded-full px-2 py-0.5">
              <div className="w-1 h-1 rounded-full bg-emerald-400" />
              <span className="text-[9px] text-emerald-400 font-medium">Caixa aberto</span>
            </div>
          </div>
        </div>

        <div className="flex h-[340px]">
          {/* Left: product area */}
          <div className="flex-1 flex flex-col border-r border-white/5 min-w-0">
            {/* Search */}
            <div className="p-3 border-b border-white/5">
              <div className="flex items-center gap-2 bg-white/5 border border-white/8 rounded-lg px-3 py-2">
                <svg className="w-3 h-3 text-slate-600 shrink-0" fill="none" viewBox="0 0 24 24" stroke="currentColor">
                  <path strokeLinecap="round" strokeLinejoin="round" strokeWidth={2} d="M21 21l-6-6m2-5a7 7 0 11-14 0 7 7 0 0114 0z" />
                </svg>
                <span className="text-[10px] text-slate-600">Buscar ou escanear produto...</span>
              </div>
            </div>

            {/* Product grid */}
            <div className="flex-1 p-2.5 grid grid-cols-2 gap-1.5 content-start overflow-hidden">
              {quickProducts.map((p) => (
                <div
                  key={p.code}
                  className="bg-white/4 border border-white/6 rounded-lg p-2 flex flex-col gap-0.5"
                >
                  <span className="text-[8px] text-slate-600 font-mono">{p.code}</span>
                  <span className="text-[10px] text-slate-300 font-medium leading-tight">{p.name}</span>
                  <span className="text-[10px] text-orken-indigo font-semibold">{p.price}</span>
                </div>
              ))}
            </div>
          </div>

          {/* Right: cart */}
          <div className="w-[155px] flex flex-col shrink-0">
            {/* Cart header */}
            <div className="px-3 py-2 border-b border-white/5">
              <p className="text-[9px] font-semibold text-slate-500 uppercase tracking-wider">Carrinho</p>
            </div>

            {/* Cart items */}
            <div className="flex-1 divide-y divide-white/4 overflow-hidden">
              {cartItems.map((item) => (
                <div key={item.name} className="px-3 py-2 space-y-0.5">
                  <p className="text-[9px] text-slate-300 leading-tight">{item.name}</p>
                  <div className="flex items-center justify-between">
                    <span className="text-[9px] text-slate-600">x{item.qty}</span>
                    <span className="text-[9px] font-semibold text-white">{item.value}</span>
                  </div>
                </div>
              ))}
            </div>

            {/* Total + CTA */}
            <div className="border-t border-white/8 p-3 space-y-2">
              <div className="flex items-center justify-between">
                <span className="text-[9px] text-slate-500">Total</span>
                <span className="text-sm font-bold text-white">R$ 92,50</span>
              </div>
              <div className="bg-orken-indigo rounded-lg py-1.5 text-center">
                <span className="text-[10px] font-semibold text-white">Finalizar venda →</span>
              </div>
            </div>
          </div>
        </div>

        {/* Status bar — shows integration happening */}
        <div className="border-t border-white/5 px-4 py-2 bg-[#0a1020] flex items-center justify-between">
          <div className="flex items-center gap-3">
            <div className="flex items-center gap-1">
              <div className="w-1 h-1 rounded-full bg-emerald-400" />
              <span className="text-[9px] text-slate-600">Estoque sincronizado</span>
            </div>
            <div className="flex items-center gap-1">
              <div className="w-1 h-1 rounded-full bg-orken-indigo" />
              <span className="text-[9px] text-slate-600">Caixa atualizado</span>
            </div>
          </div>
          <span className="text-[9px] text-slate-700">14:32</span>
        </div>
      </div>
    </div>
  );
}

// ─── Proof strip ─────────────────────────────────────────────────────────────

function ProofStrip() {
  const items = [
    { dot: "bg-emerald-500", text: "Estoque que você pode confiar"    },
    { dot: "bg-orken-indigo", text: "Caixa que fecha sem surpresa"    },
    { dot: "bg-amber-400",    text: "Reabastecimento na hora certa"   },
  ];
  return (
    <div className="flex flex-col sm:flex-row items-start sm:items-center gap-4 sm:gap-8 pt-10 border-t border-white/5">
      {items.map((item) => (
        <div key={item.text} className="flex items-center gap-2">
          <div className={`w-1.5 h-1.5 rounded-full shrink-0 ${item.dot}`} />
          <span className="text-xs text-slate-400">{item.text}</span>
        </div>
      ))}
    </div>
  );
}

// ─── Hero ─────────────────────────────────────────────────────────────────────

export function LandingHero() {
  return (
    <section className="relative bg-orken-navy pt-16 sm:pt-20 pb-20 px-5 md:px-8 overflow-hidden">
      {/* Background texture: subtle dot grid */}
      <div
        className="pointer-events-none absolute inset-0"
        style={{
          backgroundImage:
            "radial-gradient(circle, rgba(255,255,255,0.04) 1px, transparent 1px)",
          backgroundSize: "28px 28px",
        }}
      />
      {/* Distant indigo bloom */}
      <div className="pointer-events-none absolute top-0 right-0 w-[600px] h-[500px] bg-orken-indigo/5 rounded-full blur-[120px] -translate-y-1/3 translate-x-1/4" />

      <div className="relative max-w-6xl mx-auto">
        <div className="grid grid-cols-1 lg:grid-cols-[1fr_460px] gap-12 xl:gap-20 items-center">

          {/* ── Left: copy ── */}
          <div>
            {/* Eyebrow */}
            <div className="flex items-center gap-2.5 mb-7">
              <div className="h-px w-6 bg-orken-indigo" />
              <span className="text-xs font-semibold uppercase tracking-widest text-orken-indigo">
                PDV · Caixa · Estoque
              </span>
            </div>

            {/* Headline */}
            <h1 className="font-display text-[2.75rem] sm:text-[3.5rem] md:text-[4rem] font-extrabold leading-[1.05] tracking-normal text-white mb-6">
              Vendeu.
              <br />
              <span className="text-orken-indigo">Já está registrado.</span>
            </h1>

            {/* Subheadline */}
            <p className="text-base sm:text-lg text-slate-400 leading-relaxed max-w-xl mb-10">
              Cada venda atualiza o estoque, registra no caixa e alimenta o
              relatório — sem planilha, sem digitação extra, sem descobrir
              uma diferença às 23h.
            </p>

            {/* CTAs */}
            <div className="flex flex-col sm:flex-row items-start gap-3">
              <a
                href={SIGNUP_URL}
                className="inline-flex items-center gap-2 bg-orken-indigo hover:bg-orken-indigo/90 text-white font-semibold px-7 py-3.5 rounded-xl transition-colors text-[15px] whitespace-nowrap"
              >
                Começar grátis →
              </a>
              <a
                href={LOGIN_URL}
                className="inline-flex items-center gap-2 border border-white/10 hover:border-white/20 text-slate-300 hover:text-white font-medium px-7 py-3.5 rounded-xl transition-colors text-[15px] whitespace-nowrap"
              >
                Já tenho conta
              </a>
            </div>

            <p className="mt-4 text-xs text-slate-600">
              Sem cartão · Sem contrato · Está operando hoje
            </p>

            {/* Proof strip */}
            <ProofStrip />
          </div>

          {/* ── Right: mockup ── */}
          <div className="mt-4 lg:mt-0">
            <DashboardMockup />
          </div>
        </div>
      </div>
    </section>
  );
}
