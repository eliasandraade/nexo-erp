import { useState, useEffect } from "react";
import { useQuery } from "@tanstack/react-query";
import { Loader2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { toast } from "sonner";
import { useAuth } from "@/modules/auth/context/AuthContext";
import { getFoodSettings } from "../api/restaurante.api";
import { useUpdateOperationalCosts, FOOD_SETTINGS_KEY } from "../hooks/useFoodSettings";

/**
 * Per-minute gas and labor cost rates that feed the CMV calculation of the
 * recipe cards (fichas técnicas). Lives in Financeiro — next to the CMV it
 * drives — not in the table/area setup page where it was conceptually misplaced.
 */
export function OperationalCostsCard() {
  const { session } = useAuth();
  const storeId = session?.storeId ?? "";

  const { data: settings } = useQuery({
    queryKey: FOOD_SETTINGS_KEY(storeId),
    queryFn: getFoodSettings,
    enabled: !!storeId,
    staleTime: 60_000,
  });
  const updateCostsMut = useUpdateOperationalCosts(storeId);
  const [gasRate, setGasRate] = useState<string>("");
  const [laborRate, setLaborRate] = useState<string>("");

  useEffect(() => {
    if (settings) {
      setGasRate(settings.costPerMinuteGas?.toString() ?? "0");
      setLaborRate(settings.costPerMinuteLaborRate?.toString() ?? "0");
    }
  }, [settings]);

  const handleSave = () => {
    updateCostsMut.mutate(
      {
        costPerMinuteGas: parseFloat(gasRate) || 0,
        costPerMinuteLaborRate: parseFloat(laborRate) || 0,
      },
      {
        onSuccess: () => toast.success("Custos operacionais salvos!"),
        onError: () => toast.error("Erro ao salvar custos operacionais."),
      }
    );
  };

  return (
    <div className="space-y-3">
      <div>
        <h2 className="text-sm font-semibold text-foreground">Parâmetros de custo (CMV)</h2>
        <p className="text-xs text-muted-foreground mt-0.5">
          Custo por minuto de gás e mão de obra. Entram no cálculo de CMV das fichas
          técnicas exibido acima.
        </p>
      </div>

      <div className="p-4 border border-border rounded-xl space-y-4">
        <div className="grid grid-cols-2 gap-4">
          <div className="space-y-1.5">
            <Label htmlFor="gas-rate" className="text-sm">Custo de gás (por minuto)</Label>
            <div className="relative">
              <span className="absolute left-3 top-1/2 -translate-y-1/2 text-muted-foreground text-sm">R$</span>
              <Input
                id="gas-rate"
                type="number"
                min={0}
                step={0.0001}
                value={gasRate}
                onChange={(e) => setGasRate(e.target.value)}
                className="pl-9 text-sm"
                placeholder="0.0000"
              />
            </div>
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="labor-rate" className="text-sm">Custo de mão de obra (por minuto)</Label>
            <div className="relative">
              <span className="absolute left-3 top-1/2 -translate-y-1/2 text-muted-foreground text-sm">R$</span>
              <Input
                id="labor-rate"
                type="number"
                min={0}
                step={0.0001}
                value={laborRate}
                onChange={(e) => setLaborRate(e.target.value)}
                className="pl-9 text-sm"
                placeholder="0.0000"
              />
            </div>
          </div>
        </div>
        <Button size="sm" onClick={handleSave} disabled={updateCostsMut.isPending}>
          {updateCostsMut.isPending && <Loader2 className="h-4 w-4 mr-2 animate-spin" />}
          Salvar custos
        </Button>
      </div>
    </div>
  );
}
