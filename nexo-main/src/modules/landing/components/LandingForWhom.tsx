// ─── Orken Menu: the whole restaurant running in real time ───────────────────

type OrderStatus = "Pendente" | "Em preparo" | "Pronto";

const statusStyle: Record<OrderStatus, string> = {
  "Pendente":   "bg-slate-700/60 text-slate-400",
  "Em preparo": "bg-amber-500/15 text-amber-400",
  "Pronto":     "bg-emerald-500/15 text-emerald-400",
};

const ORDER_ITEMS: { name: string; qty: number; status: OrderStatus }[] = [
  { name: "Frango grelhado",  qty: 1, status: "Pronto"     },
  { name: "Risoto de funghi", qty: 1, status: "Em preparo" },
  { name: "Suco de laranja",  qty: 2, status: "Pronto"     },
  { name: "Pão de alho",      qty: 1, status: "Pendente"   },
];

function RestaurantView() {
  return (
    <div className="w-full max-w-[480px] mx-auto lg:mx-0 select-none pointer-events-none">
      <div className="rounded-2xl border border-white/8 bg-[#0d1525] overflow-hidden shadow-2xl">

        {/* Topbar */}
        <div className="flex items-center justify-between px-4 py-2.5 border-b border-white/5 bg-[#0a1020]">
          <div className="flex items-center gap-2">
            <span className="font-display text-sm font-bold text-white">
              Ork<span className="text-orken-indigo">en</span>
            </span>
            <span className="text-white/15 mx-1 text-xs">|</span>
            <span className="text-[11px] text-slate-500">Menu</span>
          </div>
          <span className="text-[9px] text-slate-600">Sábado · 20:14</span>
        </div>

        <div className="grid grid-cols-2 divide-x divide-white/5">
          {/* Mesa view */}
          <div className="p-3">
            <div className="flex items-center justify-between mb-2">
              <p className="text-[9px] font-semibold text-slate-500 uppercase tracking-wider">Mesa 04</p>
              <span className="text-[8px] bg-orken-indigo/15 text-orken-indigo px-1.5 py-0.5 rounded-full font-medium">
                Em atendimento
              </span>
            </div>
            <p className="text-[9px] text-slate-600 mb-3">Comanda #47 · 4 itens</p>
            <div className="space-y-1.5">
              {ORDER_ITEMS.map((item) => (
                <div key={item.name} className="flex items-center justify-between">
                  <span className="text-[10px] text-slate-300 truncate pr-2">{item.name}</span>
                  <span className="text-[9px] text-slate-600 shrink-0">×{item.qty}</span>
                </div>
              ))}
            </div>
            <div className="mt-3 pt-3 border-t border-white/5 flex items-center justify-between">
              <span className="text-[9px] text-slate-500">Total parcial</span>
              <span className="text-[11px] font-bold text-white">R$ 89,00</span>
            </div>
          </div>

          {/* Cozinha view */}
          <div className="p-3">
            <p className="text-[9px] font-semibold text-slate-500 uppercase tracking-wider mb-2">
              Cozinha · Ativos
            </p>
            <p className="text-[9px] text-slate-600 mb-3">3 mesas em preparo</p>
            <div className="space-y-1.5">
              {ORDER_ITEMS.map((item) => (
                <div key={item.name} className="flex items-center justify-between gap-1">
                  <span className="text-[9px] text-slate-400 truncate">{item.name}</span>
                  <span
                    className={`text-[8px] font-medium px-1.5 py-0.5 rounded-full shrink-0 ${statusStyle[item.status]}`}
                  >
                    {item.status}
                  </span>
                </div>
              ))}
            </div>

            {/* Notification */}
            <div className="mt-3 pt-3 border-t border-white/5">
              <div className="flex items-center gap-1.5 bg-emerald-500/8 border border-emerald-500/15 rounded-lg px-2 py-1.5">
                <div className="w-1 h-1 rounded-full bg-emerald-400 shrink-0" />
                <p className="text-[9px] text-emerald-400">
                  Frango pronto · <span className="font-semibold">Garçom notificado</span>
                </p>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}

export function LandingMenu() {
  return (
    <section
      id="orken-menu"
      className="bg-orken-graphite py-24 px-5 md:px-8 border-t border-white/5"
    >
      <div className="max-w-6xl mx-auto">
        <div className="grid grid-cols-1 lg:grid-cols-[1fr_480px] gap-14 xl:gap-20 items-center">

          {/* Copy */}
          <div className="order-1 lg:order-1">
            <p className="text-xs font-semibold uppercase tracking-widest text-orken-indigo mb-5">
              Orken Menu
            </p>
            <h2 className="font-display text-[2rem] sm:text-[2.5rem] md:text-[2.75rem] font-extrabold text-white leading-[1.1] tracking-normal mb-6">
              Da comanda ao prato.
              <br />
              <span className="text-orken-indigo">Em tempo real.</span>
            </h2>
            <p className="text-slate-400 text-base leading-relaxed max-w-lg mb-10">
              Mesas, cozinha e delivery no mesmo sistema. Cada pedido
              abre nas telas certas ao mesmo tempo — sem papel,
              sem comanda perdida, sem cliente esperando sem motivo.
            </p>

            {/* Operational chain — not feature list */}
            <div className="space-y-0 border-t border-white/5 pt-8">
              {[
                {
                  trigger: "Pedido feito na mesa",
                  result:  "A cozinha já aparece na fila — sem garçom ir até lá.",
                },
                {
                  trigger: "Prato pronto",
                  result:  "O garçom recebe notificação. Ninguém fica esperando na janela.",
                },
                {
                  trigger: "Mesa fechada",
                  result:  "O caixa já sabe o total. Sem redigitar, sem erro.",
                },
              ].map(({ trigger, result }, i) => (
                <div key={trigger} className={`py-5 ${i < 2 ? "border-b border-white/5" : ""}`}>
                  <p className="text-white font-semibold text-sm mb-1">{trigger}</p>
                  <p className="text-slate-500 text-sm leading-relaxed">{result}</p>
                </div>
              ))}
            </div>
          </div>

          {/* Visual */}
          <div className="order-2 lg:order-2">
            <RestaurantView />
          </div>
        </div>
      </div>
    </section>
  );
}
