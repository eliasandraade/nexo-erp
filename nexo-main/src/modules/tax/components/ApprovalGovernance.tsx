import type { TaxGovernanceDto } from "../api/tax.api";
import { APPROVAL_MODE, INTERNAL_APPROVAL, INTERNAL_APPROVAL_NOTICE, PROFESSIONAL_REVIEW, labelOf, toneOf } from "../lib/labels";
import { ToneBadge } from "./TaxBadges";

/**
 * Under which approval the rules ran: the mode, and — side by side — the internal pilot approval and the
 * professional review. The pilot approval never reads as a professional one; a pending review always shows.
 */
export function ApprovalGovernance({ governance }: Readonly<{ governance: TaxGovernanceDto }>) {
  const pilot = governance.mode === "ControlledPilot";
  const rules = governance.rules;
  const allPilot = rules.length > 0 && rules.every((r) => r.approvedForPilot);
  const pending = governance.professionalReviewPending;
  const caveats = rules.filter((r) => r.pilotCaveats);

  return (
    <div className="space-y-2 text-sm">
      <p className="flex flex-wrap items-center gap-2">
        <ToneBadge label={labelOf(APPROVAL_MODE, governance.mode)} tone={toneOf(APPROVAL_MODE, governance.mode)} title={`Política ${governance.policy}`} />
        {pilot && allPilot && (
          <ToneBadge label={INTERNAL_APPROVAL.ApprovedForControlledPilot.label} tone={INTERNAL_APPROVAL.ApprovedForControlledPilot.tone} title={INTERNAL_APPROVAL_NOTICE} />
        )}
        {pending.length > 0
          ? <ToneBadge label={PROFESSIONAL_REVIEW.Pending.label} tone={PROFESSIONAL_REVIEW.Pending.tone} />
          : rules.length > 0 && <ToneBadge label={PROFESSIONAL_REVIEW.Approved.label} tone={PROFESSIONAL_REVIEW.Approved.tone} />}
      </p>
      {pilot && governance.effectsNotice && <p className="text-xs font-medium">{governance.effectsNotice}</p>}
      {pilot && <p className="text-xs text-muted-foreground">{INTERNAL_APPROVAL_NOTICE}</p>}
      {governance.decisionReferences.length > 0 && (
        <p className="text-xs text-muted-foreground">Decisões internas registradas: {governance.decisionReferences.join(" · ")}</p>
      )}
      {governance.notApproved.length > 0 && (
        <p className="text-warning">Sem aprovação para este modo: {governance.notApproved.join(", ")}.</p>
      )}
      {pending.length > 0 && (
        <p className="text-xs text-muted-foreground">Revisão profissional pendente: {pending.join(", ")}.</p>
      )}
      {caveats.length > 0 && (
        <details>
          <summary className="cursor-pointer text-xs font-medium">Ressalvas aceitas para o piloto ({caveats.length})</summary>
          <ul className="mt-1 space-y-1 text-xs text-muted-foreground">
            {caveats.map((r) => (
              <li key={r.ruleVersionId}>
                <span className="font-mono">{r.key} v{r.version}</span>: {r.pilotCaveats}
                {r.internalApproval !== "ApprovedForControlledPilot" && ` (${labelOf(INTERNAL_APPROVAL, r.internalApproval)})`}
                {r.internalApprovalDecisionRef && <span className="block font-mono text-[11px]">decisão {r.internalApprovalDecisionRef}</span>}
              </li>
            ))}
          </ul>
        </details>
      )}
      <p className="font-mono text-[11px] text-muted-foreground">
        política {governance.policy}{governance.enforced ? "" : " · não exigida neste ambiente"}
      </p>
    </div>
  );
}
