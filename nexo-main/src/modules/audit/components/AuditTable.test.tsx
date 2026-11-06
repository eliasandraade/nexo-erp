import { render, screen } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { AuditTable } from "./AuditTable";
import type { AuditActionType, AuditRecord } from "../types";

const record = (actionType: string): AuditRecord => ({
  id: "r1",
  timestamp: "2026-11-05T12:00:00Z",
  actionType: actionType as AuditActionType,
  severity: "warning",
  actor: "user",
  entityType: "User",
  entityId: "22222222-2222-2222-2222-222222222222",
  description: "Alteração recusada: deixaria a empresa sem Diretoria ativa.",
});

describe("the audit screen of the company", () => {
  it("names the refused change of the last Diretoria", () => {
    render(<AuditTable records={[record("user_last_director_refused")]} />);

    expect(screen.getByText("Alteração recusada (última Diretoria)")).toBeInTheDocument();
    expect(screen.getByText(/deixaria a empresa sem Diretoria ativa/)).toBeInTheDocument();
  });
});
