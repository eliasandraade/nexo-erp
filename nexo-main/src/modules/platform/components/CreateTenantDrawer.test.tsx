import { describe, it, expect, vi, afterEach } from "vitest";
import { useState } from "react";
import { render, screen, fireEvent, cleanup, waitFor } from "@testing-library/react";
import { CreateTenantDrawer } from "./CreateTenantDrawer";
import { polyfillRadix } from "../test/aiFixtures";

polyfillRadix();

vi.mock("../hooks/usePlatformTenants", () => ({
  useCreateTenant: () => ({ mutateAsync: vi.fn(), isPending: false }),
}));

function Harness() {
  const [open, setOpen] = useState(false);
  return (
    <>
      <button onClick={() => setOpen(true)}>Novo cliente (abrir)</button>
      <CreateTenantDrawer open={open} onClose={() => setOpen(false)} onCreated={vi.fn()} />
    </>
  );
}

function openDrawer() {
  const opener = screen.getByRole("button", { name: "Novo cliente (abrir)" });
  opener.focus();
  fireEvent.click(opener);
  return opener;
}

describe("CreateTenantDrawer", () => {
  afterEach(() => cleanup());

  it("stays out of the page while closed", () => {
    render(<Harness />);
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it("opens as a named dialog with focus inside it", () => {
    render(<Harness />);
    openDrawer();

    const dialog = screen.getByRole("dialog", { name: "Novo cliente" });
    expect(dialog).toContainElement(document.activeElement as HTMLElement);
  });

  it("closes on Esc and returns focus to what opened it", async () => {
    render(<Harness />);
    const opener = openDrawer();

    fireEvent.keyDown(document.activeElement ?? document.body, { key: "Escape" });

    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
    await waitFor(() => expect(opener).toHaveFocus());
  });

  it("closes on the close button and on Cancelar", () => {
    render(<Harness />);

    openDrawer();
    fireEvent.click(screen.getByRole("button", { name: "Close" }));
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();

    openDrawer();
    fireEvent.click(screen.getByRole("button", { name: "Cancelar" }));
    expect(screen.queryByRole("dialog")).not.toBeInTheDocument();
  });

  it("closes on a click outside the panel", async () => {
    render(<Harness />);
    openDrawer();

    // The sheet starts listening for clicks outside only after the click that opened it has finished.
    await new Promise(resolve => setTimeout(resolve, 0));
    fireEvent.pointerDown(document.body);

    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
  });

  it("hides the rest of the page from assistive technology while open", () => {
    render(<Harness />);
    openDrawer();

    expect(screen.queryByRole("button", { name: "Novo cliente (abrir)" })).not.toBeInTheDocument();
  });
});
