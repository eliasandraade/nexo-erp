import { useState } from "react";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { Plus } from "lucide-react";
import { apiClient } from "@/services/api-client";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { toast } from "sonner";

interface PriceEntry { id: string; price: number; purchasedAt: string; }
interface PriceHistory { lastPrice: number | null; averagePrice: number | null; history: PriceEntry[]; }

const PRICE_KEY = (id: string) => ["product-purchase-prices", id] as const;

function fetchPriceHistory(productId: string): Promise<PriceHistory> {
  return apiClient.get<PriceHistory>(`/products/${productId}/purchase-prices`);
}

interface Props { productId: string; }

export function IngredientPriceSection({ productId }: Props) {
  const qc = useQueryClient();
  const [newPrice, setNewPrice] = useState("");
  const [newDate, setNewDate]   = useState(new Date().toISOString().slice(0, 10));

  const { data } = useQuery({
    queryKey: PRICE_KEY(productId),
    queryFn:  () => fetchPriceHistory(productId),
  });

  const addMut = useMutation({
    mutationFn: () =>
      apiClient.post<PriceEntry>(`/products/${productId}/purchase-prices`, {
        price: parseFloat(newPrice),
        purchasedAt: newDate,
      }),
    onSuccess: () => {
      qc.invalidateQueries({ queryKey: PRICE_KEY(productId) });
      setNewPrice("");
      toast.success("Preço registrado.");
    },
  });

  const fmt = (v: number | null) =>
    v !== null ? `R$ ${v.toFixed(4)}` : "—";

  return (
    <div className="space-y-4">
      <div className="grid grid-cols-2 gap-4">
        <div className="rounded-lg border p-3 space-y-1">
          <p className="text-xs text-muted-foreground">Última compra</p>
          <p className="text-lg font-semibold">{fmt(data?.lastPrice ?? null)}</p>
        </div>
        <div className="rounded-lg border p-3 space-y-1">
          <p className="text-xs text-muted-foreground">Preço médio (últimas 5)</p>
          <p className="text-lg font-semibold">{fmt(data?.averagePrice ?? null)}</p>
        </div>
      </div>

      <div className="flex gap-2 items-end">
        <div className="flex-1 space-y-1">
          <Label className="text-xs">Nova compra — valor (R$)</Label>
          <Input
            type="number" step="0.0001" min="0"
            placeholder="0,0000"
            value={newPrice}
            onChange={(e) => setNewPrice(e.target.value)}
          />
        </div>
        <div className="space-y-1">
          <Label className="text-xs">Data</Label>
          <Input
            type="date"
            value={newDate}
            onChange={(e) => setNewDate(e.target.value)}
          />
        </div>
        <Button
          size="sm"
          disabled={!newPrice || isNaN(parseFloat(newPrice)) || addMut.isPending}
          onClick={() => addMut.mutate()}
        >
          <Plus className="h-3.5 w-3.5 mr-1" />
          Registrar
        </Button>
      </div>

      {data?.history.length ? (
        <table className="w-full text-xs">
          <thead>
            <tr className="text-muted-foreground border-b">
              <th className="text-left pb-1">Data</th>
              <th className="text-right pb-1">Preço</th>
            </tr>
          </thead>
          <tbody>
            {data.history.map((e) => (
              <tr key={e.id} className="border-b last:border-0">
                <td className="py-1">{new Date(e.purchasedAt).toLocaleDateString("pt-BR")}</td>
                <td className="text-right">{fmt(e.price)}</td>
              </tr>
            ))}
          </tbody>
        </table>
      ) : null}
    </div>
  );
}
