import { useState } from "react";
import { toast } from "sonner";
import { cn } from "@/lib/utils";
import { useAssignMovementStage } from "../../hooks/use-build-progress";
import type { BuildStageDto } from "../../api/build.api";

const NONE = "__none__";
const UNKNOWN = "__unknown__";

interface Props {
  projectId:  string;
  movementId: string;
  description: string;
  /** Current stage when known (`undefined` = the list did not say). */
  stageId:    string | null | undefined;
  stages:     BuildStageDto[];
}

/** Compact per-expense stage picker (PUT movement stage). */
export function MovementStageSelect({ projectId, movementId, description, stageId, stages }: Props) {
  const mut = useAssignMovementStage(projectId);
  // what we last saved here wins over the list (which may not carry the stage)
  const [local, setLocal] = useState<string | null | undefined>(undefined);
  const current = local !== undefined ? local : stageId;
  const value = current === undefined ? UNKNOWN : current ?? NONE;

  const change = (v: string) => {
    if (v === UNKNOWN) return;
    const next = v === NONE ? null : v;
    mut.mutate({ movementId, stageId: next }, {
      onSuccess: (res) => {
        setLocal(res.stageId);
        toast.success(res.stageName ? `Despesa ligada à etapa ${res.stageName}.` : "Despesa sem etapa.");
      },
      onError: (e) => toast.error(e instanceof Error ? e.message : "Não foi possível mudar a etapa."),
    });
  };

  return (
    <select
      aria-label={`Etapa de ${description}`}
      value={value}
      disabled={mut.isPending}
      onChange={(e) => change(e.target.value)}
      className={cn(
        "h-7 max-w-[10rem] truncate rounded-md border border-input bg-background px-1.5 text-xs",
        "focus:outline-none focus-visible:ring-2 focus-visible:ring-ring disabled:opacity-50",
        value === UNKNOWN || value === NONE ? "text-muted-foreground" : "text-foreground",
      )}
    >
      {value === UNKNOWN && <option value={UNKNOWN}>Etapa…</option>}
      <option value={NONE}>Sem etapa</option>
      {stages.map((s) => (
        <option key={s.id} value={s.id}>{s.name}</option>
      ))}
    </select>
  );
}
