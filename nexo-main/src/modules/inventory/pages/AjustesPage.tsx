import { useNavigate } from "react-router-dom";
import { ArrowLeft } from "lucide-react";
import { Button } from "@/components/ui/button";
import { PageHeader } from "@/components/shared/PageHeader";
import { InventoryAdjustmentForm } from "../components/InventoryAdjustmentForm";

export default function AjustesPage() {
  const navigate = useNavigate();

  return (
    <div className="space-y-6">
      <PageHeader
        title="Ajuste de estoque"
        description="Registre correções manuais de saldo com rastreabilidade."
        actions={
          <Button variant="outline" onClick={() => navigate("/estoque")}>
            <ArrowLeft className="h-4 w-4 mr-1" /> Voltar
          </Button>
        }
      />
      <InventoryAdjustmentForm />
    </div>
  );
}
