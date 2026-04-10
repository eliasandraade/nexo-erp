import { useNavigate } from "react-router-dom";
import { ArrowLeft } from "lucide-react";
import { Button } from "@/components/ui/button";
import { PageHeader } from "@/components/shared/PageHeader";
import { InventoryTransferForm } from "../components/InventoryTransferForm";

export default function TransferenciasPage() {
  const navigate = useNavigate();

  return (
    <div className="space-y-6">
      <PageHeader
        title="Transferência de estoque"
        description="Transfira produtos entre lojas com registro completo da operação."
        actions={
          <Button variant="outline" onClick={() => navigate("/estoque")}>
            <ArrowLeft className="h-4 w-4 mr-1" /> Voltar
          </Button>
        }
      />
      <InventoryTransferForm />
    </div>
  );
}
