import { useState } from "react";
import { cn } from "@/lib/utils";
import { InspectionRunner } from "../quality/InspectionRunner";
import { HandoverSection } from "./HandoverSection";
import { WarrantySection } from "./WarrantySection";
import { AssistanceSection } from "./AssistanceSection";

type Section = "entrega" | "garantias" | "assistencia";

const SECTIONS: Array<{ key: Section; label: string }> = [
  { key: "entrega",     label: "Entrega" },
  { key: "garantias",   label: "Garantias" },
  { key: "assistencia", label: "Assistência técnica" },
];

/** "Entrega e pós-obra": handover (with the client walkthrough), warranty terms and technical assistance. */
export function PostConstructionTab({ projectId }: { projectId: string }) {
  const [section, setSection] = useState<Section>("entrega");
  const [running, setRunning] = useState<string | null>(null);

  if (running) return <InspectionRunner projectId={projectId} inspectionId={running} onBack={() => setRunning(null)} />;

  return (
    <div className="space-y-4">
      <div className="flex gap-1 overflow-x-auto rounded-md bg-muted p-1" role="tablist" aria-label="Seções de pós-obra">
        {SECTIONS.map((s) => (
          <button key={s.key} role="tab" aria-selected={section === s.key} onClick={() => setSection(s.key)}
            className={cn("whitespace-nowrap rounded px-3 py-1.5 text-sm font-medium",
              section === s.key ? "bg-background shadow-sm" : "text-muted-foreground hover:text-foreground")}>
            {s.label}
          </button>
        ))}
      </div>
      {section === "entrega" && <HandoverSection projectId={projectId} onRunInspection={setRunning} />}
      {section === "garantias" && <WarrantySection projectId={projectId} />}
      {section === "assistencia" && <AssistanceSection projectId={projectId} />}
    </div>
  );
}
