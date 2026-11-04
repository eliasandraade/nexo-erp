import { describe, it, expect, vi, afterEach } from "vitest";
import { render, screen, fireEvent, cleanup } from "@testing-library/react";
import { ImpersonationReasonDialog } from "./ImpersonationReasonDialog";
import { polyfillRadix } from "../test/aiFixtures";

polyfillRadix();

describe("ImpersonationReasonDialog", () => {
  afterEach(() => cleanup());

  const setup = () => {
    const onConfirm = vi.fn();
    render(<ImpersonationReasonDialog open tenantName="Padaria Sol" targetName="Ana" onConfirm={onConfirm} onCancel={vi.fn()} />);
    const field = screen.getByLabelText("Motivo do acesso");
    const submit = screen.getByRole("button", { name: "Entrar como cliente" });
    return { onConfirm, field, submit };
  };

  it("requires 10 to 300 characters and shows a counter", () => {
    const { onConfirm, field, submit } = setup();
    expect(screen.getByText(/30 minutos/)).toBeInTheDocument();
    expect(submit).toBeDisabled();
    expect(screen.getByText("0/300")).toBeInTheDocument();

    fireEvent.change(field, { target: { value: "curto" } });
    fireEvent.blur(field);
    expect(screen.getByText("5/300")).toBeInTheDocument();
    expect(screen.getByText("Escreva de 10 a 300 caracteres.")).toBeInTheDocument();
    expect(submit).toBeDisabled();

    fireEvent.change(field, { target: { value: "x".repeat(301) } });
    expect(submit).toBeDisabled();

    fireEvent.change(field, { target: { value: "  chamado #123 nota  " } });
    expect(submit).toBeEnabled();
    fireEvent.click(submit);
    expect(onConfirm).toHaveBeenCalledWith("chamado #123 nota");
  });

  it("does not count surrounding spaces", () => {
    const { field, submit } = setup();
    fireEvent.change(field, { target: { value: "   abc     " } });
    expect(screen.getByText("3/300")).toBeInTheDocument();
    expect(submit).toBeDisabled();
  });
});
