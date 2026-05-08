import { useState } from "react";
import {
  Dialog, DialogContent, DialogDescription, DialogHeader, DialogTitle, DialogFooter,
} from "@/components/ui/dialog";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import {
  Select, SelectContent, SelectItem, SelectTrigger, SelectValue,
} from "@/components/ui/select";
import { cn } from "@/lib/utils";
import { toast } from "sonner";
import { Sparkles, Loader2, CheckCircle2, ArrowLeft, AlertTriangle } from "lucide-react";
import { useAnalyzeMovement, useConfirmMovement } from "../hooks/use-interpreter";
import type { AnalyzeMovementResponse, MovementNature } from "../api/interpreter.api";

// ── Helpers ───────────────────────────────────────────────────────────────────

function fmt(v: number | null | undefined) {
  if (v == null) return "—";
  return v.toLocaleString("pt-BR", { style: "currency", currency: "BRL" });
}

function today() {
  return new Date().toISOString().slice(0, 10);
}

const NATURES: Array<{ value: MovementNature; label: string }> = [
  { value: "Expense",       label: "Despesa" },
  { value: "Transfer",      label: "Transferência" },
  { value: "Reimbursement", label: "Reembolso" },
  { value: "Advance",       label: "Adiantamento" },
];

// ── Confidence pill ───────────────────────────────────────────────────────────

function ConfidencePill({ confidence }: { confidence: number }) {
  const pct = Math.round(confidence * 100);
  return (
    <span className={cn(
      "text-[10px] px-1.5 py-0.5 rounded-full font-medium tabular-nums",
      pct >= 80 ? "bg-emerald-100 text-emerald-700 dark:bg-emerald-900/30 dark:text-emerald-400" :
      pct >= 50 ? "bg-yellow-100 text-yellow-700 dark:bg-yellow-900/30 dark:text-yellow-400" :
                  "bg-red-100 text-red-700 dark:bg-red-900/30 dark:text-red-400",
    )}>
      {pct}%
    </span>
  );
}

// ── Props ─────────────────────────────────────────────────────────────────────

interface Props {
  open:      boolean;
  onClose:   () => void;
  projectId: string;
}

// ── Component ─────────────────────────────────────────────────────────────────

type Step = "input" | "review" | "done";

export function BuildExpenseDialog({ open, onClose, projectId }: Props) {
  const [step, setStep]   = useState<Step>("input");
  const [text, setText]   = useState("");
  const [draft, setDraft] = useState<AnalyzeMovementResponse | null>(null);

  // Editable confirm fields (pre-filled from analysis, user can correct)
  const [amount,      setAmount]      = useState("");
  const [date,        setDate]        = useState(today());
  const [description, setDescription] = useState("");
  const [nature,      setNature]      = useState<MovementNature>("Expense");

  const analyzeMut = useAnalyzeMovement();
  const confirmMut = useConfirmMovement(projectId);

  // ── Reset ──────────────────────────────────────────────────────────────────

  const reset = () => {
    setStep("input"); setText(""); setDraft(null);
    setAmount(""); setDate(today()); setDescription(""); setNature("Expense");
  };

  const handleClose = () => { reset(); onClose(); };

  // ── Step 1: Analyze ────────────────────────────────────────────────────────

  const handleAnalyze = () => {
    if (!text.trim()) return;
    analyzeMut.mutate({ text: text.trim() }, {
      onSuccess: (res) => {
        setDraft(res);
        // Pre-fill editable fields from extraction
        if (res.extraction.amount.value != null)
          setAmount(String(res.extraction.amount.value));
        if (res.extraction.date.value)
          setDate(res.extraction.date.value);
        setDescription(text.trim());
        // Use suggested nature if provided
        if (res.suggestion.nature.value)
          setNature(res.suggestion.nature.value as MovementNature);
        setStep("review");
      },
      onError: (e) => {
        toast.error(e instanceof Error ? e.message : "Erro ao analisar texto.");
      },
    });
  };

  // ── Step 2: Confirm ────────────────────────────────────────────────────────

  const handleConfirm = () => {
    if (!draft) return;
    const numAmount = parseFloat(amount.replace(",", "."));
    if (!numAmount || numAmount <= 0) {
      toast.error("Valor inválido.");
      return;
    }

    confirmMut.mutate({
      draftId: draft.draftId,
      req: {
        amount:               numAmount,
        date:                 date,
        description:          description.trim() || text.trim(),
        direction:            "Out",
        nature:               nature,
        contextType:          "Obra",
        contextId:            projectId,
        originalSuggestionId: draft.suggestionId,
      },
    }, {
      onSuccess: () => {
        setStep("done");
        toast.success("Despesa registrada com sucesso!");
      },
      onError: (e) => {
        toast.error(e instanceof Error ? e.message : "Erro ao confirmar despesa.");
      },
    });
  };

  // ── Render ─────────────────────────────────────────────────────────────────

  return (
    <Dialog open={open} onOpenChange={(v) => !v && handleClose()}>
      <DialogContent className="sm:max-w-md">
        <DialogHeader>
          <DialogTitle className="flex items-center gap-2 text-base">
            <Sparkles className="h-4 w-4 text-primary" />
            {step === "done" ? "Despesa registrada" : "Registrar despesa"}
          </DialogTitle>
          <DialogDescription className="sr-only">
            Registre uma despesa da obra usando linguagem natural. O Interpreter extrai os dados automaticamente.
          </DialogDescription>
        </DialogHeader>

        {/* ── Step: input ─────────────────────────────────────────────────── */}
        {step === "input" && (
          <>
            <div className="space-y-3 py-1">
              <p className="text-sm text-muted-foreground">
                Descreva a despesa em linguagem natural. O Interpreter vai extrair os dados automaticamente.
              </p>
              <div className="space-y-1">
                <Label className="text-xs">Descrição da despesa</Label>
                <Textarea
                  value={text}
                  onChange={(e) => setText(e.target.value)}
                  placeholder="Ex: cimento 50 sacos 42 reais cada, obra torres"
                  className="min-h-[80px] resize-none text-sm"
                  autoFocus
                  onKeyDown={(e) => {
                    if (e.key === "Enter" && (e.ctrlKey || e.metaKey)) handleAnalyze();
                  }}
                />
                <p className="text-[10px] text-muted-foreground">
                  Ctrl+Enter para analisar
                </p>
              </div>

              <div className="rounded-lg bg-muted/50 p-3 text-xs text-muted-foreground space-y-1">
                <p className="font-medium text-foreground/80">Contexto aplicado automaticamente:</p>
                <p>· Tipo: <span className="font-mono text-primary">Obra</span></p>
                <p>· Direção: <span className="font-mono text-primary">Saída</span></p>
                <p>· Projeto: <span className="font-mono truncate">{projectId.slice(0, 12)}…</span></p>
              </div>
            </div>

            <DialogFooter>
              <Button variant="outline" onClick={handleClose}>Cancelar</Button>
              <Button
                onClick={handleAnalyze}
                disabled={!text.trim() || analyzeMut.isPending}
              >
                {analyzeMut.isPending ? (
                  <><Loader2 className="h-4 w-4 mr-1.5 animate-spin" /> Analisando…</>
                ) : (
                  <><Sparkles className="h-4 w-4 mr-1.5" /> Analisar</>
                )}
              </Button>
            </DialogFooter>
          </>
        )}

        {/* ── Step: review ────────────────────────────────────────────────── */}
        {step === "review" && draft && (
          <>
            <div className="space-y-4 py-1">
              {/* Extraction summary */}
              <div className="rounded-xl border border-border bg-muted/30 p-3 space-y-2">
                <p className="text-xs font-semibold text-muted-foreground uppercase tracking-wide">
                  Extração — {draft.extraction.analyzerUsed}
                </p>
                <div className="grid grid-cols-2 gap-2 text-xs">
                  <div className="flex items-center justify-between gap-1">
                    <span className="text-muted-foreground">Valor</span>
                    <div className="flex items-center gap-1">
                      <span className="font-mono">{fmt(draft.extraction.amount.value)}</span>
                      <ConfidencePill confidence={draft.extraction.amount.confidence} />
                    </div>
                  </div>
                  <div className="flex items-center justify-between gap-1">
                    <span className="text-muted-foreground">Data</span>
                    <div className="flex items-center gap-1">
                      <span className="font-mono">{draft.extraction.date.value ?? "—"}</span>
                      <ConfidencePill confidence={draft.extraction.date.confidence} />
                    </div>
                  </div>
                </div>
                {(draft.extraction.amount.confidence < 0.6 ||
                  draft.extraction.date.status === "RequiresInput") && (
                  <p className="flex items-center gap-1.5 text-xs text-yellow-600 dark:text-yellow-400">
                    <AlertTriangle className="h-3.5 w-3.5 shrink-0" />
                    Revise os campos com confiança baixa antes de confirmar.
                  </p>
                )}
              </div>

              {/* Editable fields */}
              <div className="grid grid-cols-2 gap-3">
                <div className="space-y-1">
                  <Label className="text-xs">Valor (R$) *</Label>
                  <div className="relative">
                    <span className="absolute left-2.5 top-1/2 -translate-y-1/2 text-xs text-muted-foreground">R$</span>
                    <Input
                      value={amount}
                      onChange={(e) => setAmount(e.target.value)}
                      type="number"
                      min={0}
                      step="0.01"
                      className="pl-7 text-sm"
                      placeholder="0,00"
                    />
                  </div>
                </div>
                <div className="space-y-1">
                  <Label className="text-xs">Data *</Label>
                  <Input
                    value={date}
                    onChange={(e) => setDate(e.target.value)}
                    type="date"
                    className="text-sm"
                  />
                </div>
              </div>

              <div className="space-y-1">
                <Label className="text-xs">Descrição</Label>
                <Input
                  value={description}
                  onChange={(e) => setDescription(e.target.value)}
                  className="text-sm"
                />
              </div>

              <div className="space-y-1">
                <Label className="text-xs">Natureza</Label>
                <Select value={nature} onValueChange={(v) => setNature(v as MovementNature)}>
                  <SelectTrigger className="text-sm">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    {NATURES.map((n) => (
                      <SelectItem key={n.value} value={n.value}>{n.label}</SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </div>

              {/* Fixed context info */}
              <div className="rounded-lg bg-muted/40 p-2.5 text-xs text-muted-foreground flex items-center gap-4">
                <span>Direção: <strong className="text-foreground">Saída</strong></span>
                <span>·</span>
                <span>Contexto: <strong className="text-foreground">Obra</strong></span>
                <span>·</span>
                <span className="truncate">ID: <span className="font-mono">{projectId.slice(0, 8)}…</span></span>
              </div>
            </div>

            <DialogFooter className="gap-2">
              <Button
                variant="outline"
                size="sm"
                onClick={() => setStep("input")}
                disabled={confirmMut.isPending}
              >
                <ArrowLeft className="h-3.5 w-3.5 mr-1" /> Voltar
              </Button>
              <Button
                onClick={handleConfirm}
                disabled={!amount || !date || confirmMut.isPending}
              >
                {confirmMut.isPending ? (
                  <><Loader2 className="h-4 w-4 mr-1.5 animate-spin" /> Confirmando…</>
                ) : (
                  "Confirmar despesa"
                )}
              </Button>
            </DialogFooter>
          </>
        )}

        {/* ── Step: done ──────────────────────────────────────────────────── */}
        {step === "done" && (
          <>
            <div className="py-6 flex flex-col items-center gap-3 text-center">
              <CheckCircle2 className="h-12 w-12 text-emerald-500" />
              <div>
                <p className="font-semibold">Despesa confirmada</p>
                <p className="text-sm text-muted-foreground mt-1">
                  O resumo financeiro da obra foi atualizado.
                </p>
              </div>
              <p className="text-lg font-bold tabular-nums text-primary">
                {fmt(parseFloat(amount.replace(",", ".")))}
              </p>
            </div>
            <DialogFooter>
              <Button variant="outline" onClick={() => { reset(); }}>
                Registrar outra
              </Button>
              <Button onClick={handleClose}>Fechar</Button>
            </DialogFooter>
          </>
        )}
      </DialogContent>
    </Dialog>
  );
}
