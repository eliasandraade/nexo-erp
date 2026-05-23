import { Navigate, Outlet } from "react-router-dom";
import { useAuth } from "@/modules/auth/context/AuthContext";

// ─── Operational pulse — desktop only, ambient/ghost ─────────────────────────

const EVENTS = [
  { time: "14:32", module: "PDV",     desc: "Venda finalizada · R$ 42,00"  },
  { time: "14:28", module: "ESTOQUE", desc: "Leite UHT · −4 un"            },
  { time: "14:15", module: "CAIXA",   desc: "Sangria · R$ 200,00"          },
  { time: "13:58", module: "COMPRA",  desc: "NF entrada processada"         },
  { time: "13:47", module: "PDV",     desc: "Venda finalizada · R$ 89,50"  },
] as const;

function OperationalPulse() {
  return (
    <div className="hidden xl:flex flex-col gap-4 w-[190px] shrink-0 select-none pointer-events-none opacity-[0.2]">
      <div className="flex items-center gap-2 mb-1">
        <div className="w-1 h-1 rounded-full bg-emerald-400 animate-pulse-dot" />
        <span className="text-[10px] font-semibold text-white uppercase tracking-[0.12em]">
          Atividade
        </span>
      </div>
      <div className="space-y-3.5">
        {EVENTS.map((e) => (
          <div key={e.desc} className="flex items-start gap-3">
            <span className="text-[9px] text-slate-600 font-mono mt-px shrink-0 w-7 tabular-nums">
              {e.time}
            </span>
            <div className="space-y-px">
              <span className="text-[8px] text-[#5B4DFF] font-semibold uppercase tracking-[0.1em] block">
                {e.module}
              </span>
              <span className="text-[10px] text-slate-400 leading-tight block">
                {e.desc}
              </span>
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}

// ─── Layout ───────────────────────────────────────────────────────────────────

export function AuthLayout() {
  const { session } = useAuth();

  if (session) {
    return <Navigate to="/dashboard" replace />;
  }

  return (
    <div
      className="relative bg-[#0B1020] flex flex-col overflow-hidden"
      style={{ minHeight: "100dvh" }}
    >

      {/* ── Background: dot grid ── */}
      <div
        className="pointer-events-none absolute inset-0"
        style={{
          backgroundImage:
            "radial-gradient(circle, rgba(255,255,255,0.038) 1px, transparent 1px)",
          backgroundSize: "28px 28px",
        }}
      />

      {/* ── Background: ambient bloom top-right ── */}
      <div
        className="pointer-events-none absolute top-0 right-0 w-[560px] h-[460px] rounded-full blur-[160px]"
        style={{
          background: "rgba(91, 77, 255, 0.07)",
          transform: "translate(28%, -25%)",
        }}
      />

      {/* ── Background: ambient bloom bottom-left ── */}
      <div
        className="pointer-events-none absolute bottom-0 left-0 w-[340px] h-[340px] rounded-full blur-[130px]"
        style={{
          background: "rgba(91, 77, 255, 0.045)",
          transform: "translate(-30%, 32%)",
        }}
      />

      {/* ── Wordmark ── */}
      <header className="relative z-10 px-6 sm:px-10 pt-7 sm:pt-8">
        <a
          href="/"
          className="inline-block font-display text-[20px] font-bold text-white tracking-tight select-none hover:opacity-80 transition-opacity"
        >
          Ork<span className="text-[#5B4DFF]">en</span>
        </a>
      </header>

      {/* ── Main: form + operational pulse ── */}
      <main className="relative z-10 flex flex-1 items-center justify-center px-5 py-8 sm:py-10 overflow-y-auto">
        <div className="flex items-center gap-16 xl:gap-20">
          {/* Form slot — single Outlet, never duplicated */}
          <div className="w-full max-w-[340px]">
            <Outlet />
          </div>

          {/* Ambient activity log — xl only, no layout impact on smaller screens */}
          <OperationalPulse />
        </div>
      </main>

      {/* ── Trust footer ── */}
      <footer className="relative z-10 pb-7 sm:pb-8 px-6">
        <div className="flex flex-wrap items-center justify-center gap-x-4 gap-y-1">
          <div className="flex items-center gap-1.5">
            <div
              className="w-1.5 h-1.5 rounded-full bg-emerald-500 animate-pulse-dot"
              style={{ boxShadow: "0 0 5px rgba(16, 185, 129, 0.6)" }}
            />
            <span className="text-[11px] text-slate-600 tracking-wide">
              Sistema operando
            </span>
          </div>
          <span className="text-slate-700 text-[11px]" aria-hidden>·</span>
          <span className="text-[11px] text-slate-600 tracking-wide">
            Conexão segura
          </span>
          <span className="text-slate-700 text-[11px]" aria-hidden>·</span>
          <span className="text-[11px] text-slate-600 tracking-wide">
            LGPD
          </span>
        </div>
      </footer>

    </div>
  );
}
