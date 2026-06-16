import {
  createContext,
  useContext,
  useEffect,
  useMemo,
  useState,
  type ReactNode,
} from "react";
import { useLocation } from "react-router-dom";
import { useAuth } from "@/modules/auth/context/AuthContext";
import { availableWorkspaces, workspaceForPath } from "./config";
import { readLastWorkspace, writeLastWorkspace } from "./persistence";
import type { WorkspaceDef, WorkspaceId } from "./types";

interface WorkspaceContextValue {
  /** Workspaces the tenant has an active module for. */
  available: WorkspaceDef[];
  /** The workspace currently in scope (drives the sidebar). Null when none apply. */
  active: WorkspaceDef | null;
  /** Persist + switch the active workspace. Caller handles navigation. */
  setActive: (id: WorkspaceId) => void;
}

const WorkspaceContext = createContext<WorkspaceContextValue | null>(null);

export function WorkspaceProvider({ children }: { children: ReactNode }) {
  const { session } = useAuth();
  const { pathname } = useLocation();

  // Last explicit choice, seeded from storage. Re-seeds when the user changes.
  const [chosen, setChosen] = useState<WorkspaceId | null>(() =>
    session ? readLastWorkspace(session) : null
  );

  useEffect(() => {
    setChosen(session ? readLastWorkspace(session) : null);
  }, [session?.userId, session?.tenantId]); // eslint-disable-line react-hooks/exhaustive-deps

  const available = useMemo(
    () => (session ? availableWorkspaces(session) : []),
    [session?.modules] // eslint-disable-line react-hooks/exhaustive-deps
  );

  const active = useMemo<WorkspaceDef | null>(() => {
    if (available.length === 0) return null;

    // 1. Explicit, still-valid choice wins.
    if (chosen) {
      const match = available.find((w) => w.id === chosen);
      if (match) return match;
    }
    // 2. Otherwise infer from the route the user deep-linked into.
    const inferredId = workspaceForPath(pathname);
    if (inferredId) {
      const match = available.find((w) => w.id === inferredId);
      if (match) return match;
    }
    // 3. Fall back to the first available so the sidebar stays coherent.
    return available[0];
  }, [available, chosen, pathname]);

  const value = useMemo<WorkspaceContextValue>(
    () => ({
      available,
      active,
      setActive: (id: WorkspaceId) => {
        if (session) writeLastWorkspace(session, id);
        setChosen(id);
      },
    }),
    [available, active, session]
  );

  return <WorkspaceContext.Provider value={value}>{children}</WorkspaceContext.Provider>;
}

export function useWorkspace(): WorkspaceContextValue {
  const ctx = useContext(WorkspaceContext);
  if (!ctx) throw new Error("useWorkspace must be used inside WorkspaceProvider");
  return ctx;
}
