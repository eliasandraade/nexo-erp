import { useState } from "react";
import { useNavigate, useSearchParams } from "react-router-dom";
import { Info } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { SectionCard } from "@/components/shared/SectionCard";
import { useStockItems } from "../hooks/use-stock";
import { useAdjustStock } from "../hooks/use-stock";
import { ADJUSTABLE_MOVEMENT_TYPES } from "../types";
import type { AdjustableMovementType } from "../types";

export function InventoryAdjustmentForm() {
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const preselectedProduct = searchParams.get("productId") ?? "";

  const [productId, setProductId] = useState(preselectedProduct);
  const [movementType, setMovementType] = useState<AdjustableMovementType>("ManualEntry");
  const [quantity, setQuantity] = useState("");
  const [notes, setNotes] = useState("");

  const { data: stockItems = [], isLoading: loadingStock } = useStockItems();
  const adjustMutation = useAdjustStock();

  const handleSubmit = async () => {
    if (!productId) {
      toast.error("Selecione um produto.");
      return;
    }
    const qty = Number(quantity);
    if (!qty || qty <= 0) {
      toast.error("Informe uma quantidade maior que zero.");
      return;
    }

    try {
      await adjustMutation.mutateAsync({ productId, quantity: qty, movementType, notes: notes || undefined });
      toast.success("Ajuste de estoque registrado com sucesso.");
      navigate("/estoque");
    } catch {
      toast.error("Não foi possível registrar o ajuste. Tente novamente.");
    }
  };

  return (
    <div className="space-y-6 max-w-2xl">
      <div className="flex items-start gap-3 rounded-lg border border-border bg-secondary/5 p-4">
        <Info className="h-5 w-5 text-secondary mt-0.5 shrink-0" />
        <p className="text-sm text-muted-foreground">
          Todo ajuste fica registrado no histórico de movimentações do produto e não pode ser desfeito.
        </p>
      </div>

      <SectionCard title="Dados do ajuste">
        <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
          <div className="space-y-1.5 md:col-span-2">
            <Label>Produto *</Label>
            <Select value={productId} onValueChange={setProductId} disabled={loadingStock}>
              <SelectTrigger>
                <SelectValue placeholder={loadingStock ? "Carregando..." : "Selecione o produto"} />
              </SelectTrigger>
              <SelectContent>
                {stockItems.map((s) => (
                  <SelectItem key={s.productId} value={s.productId}>
                    <span className="font-mono text-xs text-muted-foreground mr-2">
                      {s.productCode}
                    </span>
                    {s.productName}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>

          <div className="space-y-1.5">
            <Label>Tipo de ajuste *</Label>
            <Select value={movementType} onValueChange={(v) => setMovementType(v as AdjustableMovementType)}>
              <SelectTrigger><SelectValue /></SelectTrigger>
              <SelectContent>
                {ADJUSTABLE_MOVEMENT_TYPES.map((t) => (
                  <SelectItem key={t.value} value={t.value}>{t.label}</SelectItem>
                ))}
              </SelectContent>
            </Select>
          </div>

          <div className="space-y-1.5">
            <Label>Quantidade *</Label>
            <Input
              type="number"
              min={1}
              step={1}
              placeholder="0"
              value={quantity}
              onChange={(e) => setQuantity(e.target.value)}
            />
          </div>

          <div className="space-y-1.5 md:col-span-2">
            <Label>Observações</Label>
            <Textarea
              placeholder="Motivo do ajuste, referência de documento, etc. (opcional)"
              value={notes}
              onChange={(e) => setNotes(e.target.value)}
              rows={3}
            />
          </div>
        </div>
      </SectionCard>

      <div className="flex items-center justify-end gap-3">
        <Button variant="outline" onClick={() => navigate("/estoque")} disabled={adjustMutation.isPending}>
          Cancelar
        </Button>
        <Button onClick={handleSubmit} disabled={adjustMutation.isPending || loadingStock}>
          {adjustMutation.isPending ? "Salvando…" : "Confirmar ajuste"}
        </Button>
      </div>
    </div>
  );
}
