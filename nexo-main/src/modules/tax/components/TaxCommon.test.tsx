import { useState } from "react";
import { describe, it, expect, vi, afterEach, beforeEach } from "vitest";
import { screen, cleanup, fireEvent, render, waitFor } from "@testing-library/react";
import { toast } from "sonner";
import { ApiError } from "@/services/api-client";
import { downloadFile } from "@/services/pdf.api";
import { DownloadButton, FilePicker, TaxPager, TaxQueryError, TaxRefusal, TaxWarning, toastError, useReasonDialog } from "./TaxCommon";

vi.mock("sonner", () => ({ toast: { success: vi.fn(), error: vi.fn() } }));
vi.mock("@/services/pdf.api", () => ({ downloadFile: vi.fn() }));

const pdf = () => new File(["%PDF-1.7"], "contrato.pdf", { type: "application/pdf" });

describe("TaxRefusal", () => {
  afterEach(cleanup);

  it("renders nothing without an error", () => {
    const { container } = render(<TaxRefusal error={null} />);
    expect(container).toBeEmptyDOMElement();
  });

  it("frames a known refusal with its title, the server message, every reason and the action", () => {
    render(<TaxRefusal error={new ApiError(422, "Fora do piloto.", "tax_module_suspended", "req-1", ["Regra A sem aprovação", "Regra B sem aprovação"])} />);

    const alert = screen.getByRole("alert");
    expect(alert).toHaveTextContent("Módulo suspenso pela plataforma");
    expect(screen.getByText("Fora do piloto.")).toBeInTheDocument();
    expect(screen.getByText("Regra A sem aprovação")).toBeInTheDocument();
    expect(screen.getByText("Regra B sem aprovação")).toBeInTheDocument();
    expect(screen.getByText("O histórico continua disponível para consulta e verificação.")).toBeInTheDocument();
  });

  it("does not repeat a single reason as a list and falls back to a neutral title", () => {
    render(<TaxRefusal error={new ApiError(422, "Recusado.", undefined, undefined, ["Recusado."])} />);

    expect(screen.getByText("Não foi possível concluir")).toBeInTheDocument();
    expect(screen.queryByRole("listitem")).not.toBeInTheDocument();
  });

  it("explains a non-API error by its text", () => {
    render(<TaxRefusal error="falhou" />);
    expect(screen.getByText("falhou")).toBeInTheDocument();
  });
});

describe("TaxQueryError", () => {
  afterEach(cleanup);

  it("says when the profile cannot see the data", () => {
    render(<TaxQueryError error={new ApiError(403, "proibido")} what="as políticas" />);
    expect(screen.getByText("Sem permissão")).toBeInTheDocument();
    expect(screen.getByText("Seu perfil não pode ver as políticas. Peça acesso à diretoria.")).toBeInTheDocument();
  });

  it("says when the record does not exist", () => {
    render(<TaxQueryError error={new ApiError(404, "não existe")} what="a oportunidade" />);
    expect(screen.getByText("A oportunidade não encontrado(a)")).toBeInTheDocument();
  });

  it("explains a refusal instead of offering a retry", () => {
    render(<TaxQueryError error={new ApiError(422, "Fora do recorte.", "tax_module_suspended")} what="o cálculo" />);
    expect(screen.getByRole("alert")).toHaveTextContent("Fora do recorte.");
    expect(screen.queryByText("Tentar novamente")).not.toBeInTheDocument();
  });

  it("offers a retry for anything else", () => {
    const retry = vi.fn();
    render(<TaxQueryError error={new Error("rede")} what="o histórico" onRetry={retry} />);
    expect(screen.getByText("Não foi possível carregar o histórico")).toBeInTheDocument();
    fireEvent.click(screen.getByText("Tentar novamente"));
    expect(retry).toHaveBeenCalled();
  });
});

describe("toastError", () => {
  beforeEach(() => vi.clearAllMocks());

  it("titles the toast by the refusal code and describes it with the message", () => {
    toastError(new ApiError(422, "Suspenso.", "tax_module_suspended"));
    expect(toast.error).toHaveBeenCalledWith("Módulo suspenso pela plataforma", { description: "Suspenso." });
  });

  it("has no description for something that is not an error", () => {
    toastError("x");
    expect(toast.error).toHaveBeenCalledWith("Não foi possível concluir", { description: undefined });
  });
});

describe("DownloadButton", () => {
  beforeEach(() => vi.clearAllMocks());
  afterEach(cleanup);

  it("downloads through the authorised API and shows progress", async () => {
    let finish: () => void = () => {};
    vi.mocked(downloadFile).mockReturnValue(new Promise<void>((r) => { finish = r; }));
    render(<DownloadButton path="/tax/evidence-files/f1/content" fileName="nota.pdf" />);

    fireEvent.click(screen.getByRole("button", { name: "Baixar" }));
    expect(await screen.findByRole("button", { name: "Baixando…" })).toBeDisabled();
    expect(downloadFile).toHaveBeenCalledWith("/tax/evidence-files/f1/content", "nota.pdf", "Falha ao baixar");
    finish();
    expect(await screen.findByRole("button", { name: "Baixar" })).toBeEnabled();
  });

  it("toasts the failure and frees the button", async () => {
    vi.mocked(downloadFile).mockRejectedValue(new Error("Falha ao baixar: 403"));
    render(<DownloadButton path="/x" fileName="x.zip" label="Exportar" />);

    fireEvent.click(screen.getByRole("button", { name: "Exportar" }));
    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Falha ao baixar: 403"));
    expect(screen.getByRole("button", { name: "Exportar" })).toBeEnabled();
  });

  it("falls back to a generic message for a non-Error failure", async () => {
    vi.mocked(downloadFile).mockRejectedValue("boom");
    render(<DownloadButton path="/x" fileName="x.zip" />);

    fireEvent.click(screen.getByRole("button", { name: "Baixar" }));
    await waitFor(() => expect(toast.error).toHaveBeenCalledWith("Falha ao baixar"));
  });
});

function Picker() {
  const [file, setFile] = useState<File | null>(null);
  return <FilePicker id="pick" file={file} onChange={setFile} />;
}

describe("FilePicker", () => {
  afterEach(cleanup);

  it("picks one file, shows its name and size, and removes it", () => {
    const { container } = render(<Picker />);
    const input = container.querySelector<HTMLInputElement>("#pick")!;
    const click = vi.spyOn(input, "click");
    expect(input).toHaveAttribute("accept", ".pdf,.png,.jpg,.jpeg,.webp,.xml");
    expect(screen.getByText("PDF, imagem ou XML — opcional")).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Escolher arquivo" }));
    expect(click).toHaveBeenCalled();

    fireEvent.change(input, { target: { files: [pdf()] } });
    expect(screen.getByText("contrato.pdf (0 KB)")).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Trocar arquivo" })).toBeInTheDocument();

    fireEvent.click(screen.getByRole("button", { name: "Remover" }));
    expect(screen.getByText("PDF, imagem ou XML — opcional")).toBeInTheDocument();
    expect(screen.queryByRole("button", { name: "Remover" })).not.toBeInTheDocument();
  });

  it("treats an emptied selection as no file", () => {
    const onChange = vi.fn();
    const { container } = render(<FilePicker id="p2" file={null} onChange={onChange} disabled />);
    fireEvent.change(container.querySelector("#p2")!, { target: { files: [] } });
    expect(onChange).toHaveBeenCalledWith(null);
    expect(screen.getByRole("button", { name: "Escolher arquivo" })).toBeDisabled();
  });
});

describe("TaxPager", () => {
  afterEach(cleanup);

  it("only counts the records when they fit in one page", () => {
    render(<TaxPager total={3} skip={0} take={25} onSkip={vi.fn()} />);
    expect(screen.getByText("3 registro(s)")).toBeInTheDocument();
    expect(screen.queryByText("2")).not.toBeInTheDocument();
  });

  it("turns a page into the skip of the server", () => {
    const onSkip = vi.fn();
    render(<TaxPager total={60} skip={0} take={25} onSkip={onSkip} />);
    expect(screen.getByText("60 registro(s)")).toBeInTheDocument();
    fireEvent.click(screen.getByText("3"));
    expect(onSkip).toHaveBeenCalledWith(50);
  });
});

describe("TaxWarning", () => {
  afterEach(cleanup);
  it("shows the reason an action is unavailable", () => {
    render(<TaxWarning>Armazenamento indisponível.</TaxWarning>);
    expect(screen.getByText("Armazenamento indisponível.")).toBeInTheDocument();
  });
});

function Asker({ onConfirm, danger }: Readonly<{ onConfirm: (r: string) => void; danger?: boolean }>) {
  const [dialog, ask] = useReasonDialog();
  return (
    <>
      {dialog}
      <button type="button" onClick={() => ask({ title: "Revogar arquivo", description: "Continua registrado.", confirmLabel: "Revogar", danger, onConfirm })}>abrir</button>
      <button type="button" onClick={() => ask({ title: "Sem descrição", onConfirm })}>abrir simples</button>
    </>
  );
}

describe("useReasonDialog", () => {
  afterEach(cleanup);

  it("requires a reason before confirming and sends it trimmed", async () => {
    const onConfirm = vi.fn();
    render(<Asker onConfirm={onConfirm} danger />);

    fireEvent.click(screen.getByRole("button", { name: "abrir" }));
    expect(await screen.findByRole("dialog")).toHaveTextContent("Revogar arquivo");
    expect(screen.getByText("Continua registrado.")).toBeInTheDocument();
    expect(screen.getByText("Fica registrado na trilha de auditoria.")).toBeInTheDocument();
    const confirm = screen.getByRole("button", { name: "Revogar" });
    expect(confirm).toBeDisabled();

    fireEvent.change(screen.getByLabelText("Motivo"), { target: { value: "   " } });
    expect(confirm).toBeDisabled();
    fireEvent.submit(screen.getByLabelText("Motivo").closest("form")!);
    expect(onConfirm).not.toHaveBeenCalled();

    fireEvent.change(screen.getByLabelText("Motivo"), { target: { value: "  documento errado  " } });
    expect(confirm).toBeEnabled();
    fireEvent.click(confirm);
    expect(onConfirm).toHaveBeenCalledWith("documento errado");
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
  });

  it("starts empty every time and cancels without recording", async () => {
    const onConfirm = vi.fn();
    render(<Asker onConfirm={onConfirm} />);

    fireEvent.click(screen.getByRole("button", { name: "abrir" }));
    fireEvent.change(await screen.findByLabelText("Motivo"), { target: { value: "rascunho" } });
    fireEvent.click(screen.getByRole("button", { name: "Cancelar" }));
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());

    fireEvent.click(screen.getByRole("button", { name: "abrir simples" }));
    expect(await screen.findByLabelText("Motivo")).toHaveValue("");
    expect(screen.getByRole("button", { name: "Confirmar" })).toBeDisabled();
    fireEvent.keyDown(screen.getByRole("dialog"), { key: "Escape" });
    await waitFor(() => expect(screen.queryByRole("dialog")).not.toBeInTheDocument());
    expect(onConfirm).not.toHaveBeenCalled();
  });
});
