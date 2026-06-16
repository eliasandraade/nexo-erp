import { Navigate, useNavigate, Link } from "react-router-dom";
import { ArrowRight, Lock, LifeBuoy, CreditCard, LogOut } from "lucide-react";
import { useAuth } from "@/modules/auth/context/AuthContext";
import { useWorkspace } from "../WorkspaceContext";
import { WORKSPACES, availableWorkspaces } from "../config";
import type { WorkspaceDef } from "../types";

function greeting(): string {
  const h = new Date().getHours();
  if (h < 12) return "Bom dia";
  if (h < 18) return "Boa tarde";
  return "Boa noite";
}

// ─── Workspace card ───────────────────────────────────────────────────────────

function WorkspaceCard({
  workspace,
  locked,
  onEnter,
}: {
  workspace: WorkspaceDef;
  locked: boolean;
  onEnter: () => void;
}) {
  const Icon = workspace.icon;

  const inner = (
    <>
      {/* Accent edge */}
      <span
        className="absolute inset-x-0 top-0 h-[2px] rounded-t-2xl"
        style={{ background: locked ? "transparent" : workspace.accent }}
      />
      <div className="flex items-start justify-between">
        <div
          className="flex h-11 w-11 items-center justify-center rounded-xl"
          style={{
            background: locked ? "rgba(255,255,255,0.04)" : `${workspace.accent}1f`,
            color: locked ? "#5b6b8c" : workspace.accent,
          }}
        >
          <Icon className="h-[22px] w-[22px]" strokeWidth={1.75} />
        </div>
        {locked ? (
          <span className="flex items-center gap-1 text-[10.5px] font-semibold uppercase tracking-[0.1em] text-slate-500">
            <Lock className="h-3 w-3" /> Bloqueado
          </span>
        ) : (
          <span className="text-[10.5px] font-semibold uppercase tracking-[0.1em] text-emerald-400/80">
            Ativo
          </span>
        )}
      </div>

      <div className="mt-5">
        <h2 className="font-display text-[19px] font-bold tracking-tight text-white">
          {workspace.name}
        </h2>
        <p className="mt-1.5 text-[13px] leading-relaxed text-slate-400">
          {workspace.description}
        </p>
      </div>

      <div className="mt-6 flex items-center gap-1.5 text-[13px] font-semibold">
        {locked ? (
          <span className="text-slate-500 group-hover:text-slate-300 transition-colors">
            Conhecer planos
          </span>
        ) : (
          <span style={{ color: workspace.accent }}>Entrar</span>
        )}
        <ArrowRight
          className="h-4 w-4 transition-transform duration-200 group-hover:translate-x-0.5"
          style={{ color: locked ? "#64748b" : workspace.accent }}
        />
      </div>
    </>
  );

  const cardClass =
    "group relative flex w-full flex-col rounded-2xl border p-5 text-left transition-all duration-200 " +
    (locked
      ? "border-white/[0.06] bg-white/[0.015] hover:border-white/[0.12]"
      : "border-white/[0.08] bg-white/[0.025] hover:-translate-y-0.5 hover:border-white/[0.16] hover:bg-white/[0.04]");

  if (locked) {
    return (
      <Link to="/assinatura" className={cardClass}>
        {inner}
      </Link>
    );
  }
  return (
    <button type="button" onClick={onEnter} className={cardClass}>
      {inner}
    </button>
  );
}

// ─── Page ─────────────────────────────────────────────────────────────────────

export default function ModuleSelectionPage() {
  const { session, logout } = useAuth();
  const { setActive } = useWorkspace();
  const navigate = useNavigate();

  if (!session) return <Navigate to="/login" replace />;

  const available = availableWorkspaces(session);

  // One workspace → there's nothing to choose; go straight in.
  if (available.length === 1) return <Navigate to={available[0].home} replace />;

  const hasAny = available.length > 0;
  const firstName = session.name?.split(" ")[0] ?? "";

  function enter(ws: WorkspaceDef) {
    setActive(ws.id);
    navigate(ws.home, { replace: true });
  }

  return (
    <div className="relative min-h-screen overflow-hidden bg-[#0B1020] text-white">
      {/* Ambient indigo wash — single static radial, no blur filter (cheap to
          composite on low-end devices the operators actually use) */}
      <div
        className="pointer-events-none absolute -top-32 left-1/2 h-[360px] w-[680px] -translate-x-1/2"
        style={{
          background:
            "radial-gradient(ellipse at center, rgba(91,77,255,0.18) 0%, transparent 65%)",
        }}
        aria-hidden
      />

      {/* Top bar */}
      <header className="relative flex items-center justify-between px-6 py-5 sm:px-10">
        <img
          src="/orken_darkmode.png"
          alt="Orken"
          className="h-6 w-auto select-none"
          draggable={false}
        />
        <button
          onClick={logout}
          className="flex items-center gap-1.5 rounded-lg px-2.5 py-1.5 text-[12.5px] text-slate-400 transition-colors hover:bg-white/[0.04] hover:text-white"
        >
          <LogOut className="h-3.5 w-3.5" />
          Sair
        </button>
      </header>

      {/* Body */}
      <main className="relative mx-auto w-full max-w-3xl px-6 pb-20 pt-8 sm:px-10 sm:pt-14">
        <div className="mb-3 flex items-center gap-2">
          <span className="h-px w-5 bg-[#5B4DFF]" />
          <span className="text-[11px] font-semibold uppercase tracking-[0.12em] text-[#5B4DFF]">
            {firstName ? `${greeting()}, ${firstName}` : "Bem-vindo"}
          </span>
        </div>

        {hasAny ? (
          <>
            <h1 className="max-w-lg font-display text-[28px] font-bold leading-[1.12] tracking-tight sm:text-[32px]">
              Escolha sua área de trabalho
            </h1>
            <p className="mt-2.5 max-w-md text-[14px] leading-relaxed text-slate-400">
              Cada área abre só o que importa para aquela operação. Você troca quando quiser.
            </p>

            <div className="mt-9 grid gap-4 sm:grid-cols-2">
              {WORKSPACES.map((ws) => (
                <WorkspaceCard
                  key={ws.id}
                  workspace={ws}
                  locked={!available.some((a) => a.id === ws.id)}
                  onEnter={() => enter(ws)}
                />
              ))}
            </div>
          </>
        ) : (
          <NoActiveModule />
        )}
      </main>
    </div>
  );
}

// ─── Caso 3: no active module ─────────────────────────────────────────────────

function NoActiveModule() {
  const { logout } = useAuth();

  return (
    <>
      <h1 className="max-w-lg font-display text-[28px] font-bold leading-[1.12] tracking-tight sm:text-[32px]">
        Nenhum módulo ativo
      </h1>
      <p className="mt-2.5 max-w-md text-[14px] leading-relaxed text-slate-400">
        Sua conta ainda não tem uma área de trabalho liberada. Escolha um plano para
        começar ou fale com o administrador da sua empresa.
      </p>

      <div className="mt-8 flex flex-wrap items-center gap-3">
        <Link
          to="/assinatura"
          className="inline-flex h-11 items-center gap-2 rounded-[10px] bg-[#5B4DFF] px-5 text-[13.5px] font-semibold text-white transition-colors hover:bg-[#4338CA]"
        >
          <CreditCard className="h-4 w-4" />
          Ver planos
        </Link>
        <a
          href="mailto:suporte@orken.com.br"
          className="inline-flex h-11 items-center gap-2 rounded-[10px] border border-white/[0.1] px-5 text-[13.5px] font-medium text-slate-300 transition-colors hover:border-white/20 hover:text-white"
        >
          <LifeBuoy className="h-4 w-4" />
          Falar com o suporte
        </a>
        <button
          onClick={logout}
          className="inline-flex h-11 items-center gap-2 rounded-[10px] px-3 text-[13.5px] font-medium text-slate-500 transition-colors hover:text-slate-300"
        >
          <LogOut className="h-4 w-4" />
          Sair da conta
        </button>
      </div>

      {/* Locked previews so the user sees what Orken offers */}
      <div className="mt-12 border-t border-white/[0.06] pt-8">
        <p className="mb-4 text-[10.5px] font-semibold uppercase tracking-[0.14em] text-slate-500">
          Disponível no Orken
        </p>
        <div className="grid gap-4 sm:grid-cols-2">
          {WORKSPACES.map((ws) => (
            <WorkspaceCard key={ws.id} workspace={ws} locked onEnter={() => {}} />
          ))}
        </div>
      </div>
    </>
  );
}
