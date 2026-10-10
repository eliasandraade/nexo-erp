import { Navigate, useNavigate, Link } from "react-router-dom";
import { useAuth } from "@/modules/auth/context/AuthContext";
import { Brand } from "@/components/shared/Brand";
import { Button } from "@/components/ui/button";
import { useWorkspace } from "../WorkspaceContext";
import { WORKSPACES, availableWorkspaces } from "../config";
import type { WorkspaceDef } from "../types";
export default function ModuleSelectionPage() {
  const { session, logout } = useAuth();
  const { setActive } = useWorkspace();
  const navigate = useNavigate();
  if (!session) return <Navigate to="/login" replace />;
  const available = availableWorkspaces(session);
  if (available.length === 1) return <Navigate to={available[0].home} replace />;
  function enter(workspace: WorkspaceDef) { setActive(workspace.id); navigate(workspace.home, {replace:true}); }
  return <div className="min-h-dvh bg-background">
    <header className="flex items-center justify-between border-b border-border px-6 py-6 sm:px-10"><Brand /><Button variant="ghost" onClick={logout}>Sair</Button></header>
    <main className="mx-auto max-w-3xl px-6 py-12 sm:py-20">
      <p className="mb-3 break-words text-sm text-muted-foreground">{session.companyName || session.name}</p>
      <h1 className="text-3xl font-semibold tracking-tight">{available.length ? "Onde vamos trabalhar?" : "Nenhuma área liberada"}</h1>
      <p className="mt-3 max-w-prose text-sm text-muted-foreground">{available.length ? "Escolha a operação. Seus cadastros e configurações continuam disponíveis em cada área." : "Fale com o administrador da empresa ou consulte os módulos da assinatura."}</p>
      <div className="mt-10 record-list">
        {WORKSPACES.map(workspace => {
          const unlocked = available.some(item => item.id === workspace.id);
          const contents = <><div className="min-w-0"><h2 className="text-lg font-semibold">{workspace.name}</h2><p className="mt-1 text-sm text-muted-foreground">{workspace.description}</p></div><span className="shrink-0 text-sm text-primary">{unlocked ? "Entrar →" : "Ver planos →"}</span></>;
          return unlocked ? <button key={workspace.id} onClick={() => enter(workspace)} className="record-row w-full text-left hover:bg-muted">{contents}</button> : <Link key={workspace.id} to="/assinatura" className="record-row hover:bg-muted">{contents}</Link>;
        })}
      </div>
      {!available.length && <a className="mt-6 inline-flex min-h-11 items-center text-sm text-primary underline" href="mailto:suporte@orken.com.br">Falar com o suporte</a>}
    </main>
  </div>;
}
