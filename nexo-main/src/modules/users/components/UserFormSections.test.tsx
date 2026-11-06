import { fireEvent, render, screen, within } from "@testing-library/react";
import { describe, expect, it, vi } from "vitest";
import { polyfillRadix } from "@/modules/platform/test/aiFixtures";
import { UserMainDataSection } from "./UserFormSections";
import { statusLabels, statusVariant, type UserFormInput, type UserStatus } from "../types";

polyfillRadix();

const form = (status: UserStatus): UserFormInput => ({
  name: "Pessoa", email: "pessoa@orken.test", login: "pessoa", phone: "", role: "vendedor", company: "", store: "", status,
  password: "", passwordConfirm: "", requirePasswordChange: false, blocked: false, notes: "",
});

const statusOptions = () => {
  fireEvent.keyDown(screen.getByRole("combobox", { name: "Status" }), { key: "Enter" });
  return within(screen.getByRole("listbox")).getAllByRole("option").map((o) => o.textContent);
};

describe("the status of the user form", () => {
  it("shows a pending user as waiting for verification and does not offer to activate", () => {
    render(<UserMainDataSection form={form("pendingverification")} onChange={vi.fn()} errors={{}} />);

    expect(screen.getByRole("combobox", { name: "Status" }).textContent).toContain("Aguardando verificação");
    expect(statusOptions()).toEqual(["Aguardando verificação", "Inativo"]);
  });

  it("tells who manages the account how a pending user gets access", () => {
    render(<UserMainDataSection form={form("pendingverification")} onChange={vi.fn()} errors={{}} />);

    expect(screen.getByText(/link enviado por e-mail/i)).toBeInTheDocument();
    expect(screen.getByText(/Reenviar convite/i)).toBeInTheDocument();
  });

  it("keeps active and inactive for everyone else, with no mention of the link", () => {
    render(<UserMainDataSection form={form("active")} onChange={vi.fn()} errors={{}} />);

    expect(screen.queryByText(/link enviado por e-mail/i)).toBeNull();
    expect(statusOptions()).toEqual(["Ativo", "Inativo"]);
  });

  it("has a label and a color for the status, for the list and the summary", () => {
    expect(statusLabels.pendingverification).toBe("Aguardando verificação");
    expect(statusVariant.pendingverification).toBeDefined();
  });
});
