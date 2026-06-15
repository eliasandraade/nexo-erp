import { useState } from "react";
import { Loader2, Search } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Switch } from "@/components/ui/switch";
import { lookupCnpj } from "@/services/integrations.api";
import type { CustomerFormInput } from "../types";

interface Props {
  data: CustomerFormInput;
  onChange: (field: keyof CustomerFormInput, value: unknown) => void;
  isEdit?: boolean;
}

export function CustomerMainDataSection({ data, onChange, isEdit = false }: Props) {
  const isCompany = data.personType === "Company";
  const [cnpjLoading, setCnpjLoading] = useState(false);

  const handleCnpjLookup = async () => {
    const digits = data.documentNumber.replace(/\D/g, "");
    if (digits.length !== 14) {
      toast.error("Digite o CNPJ completo (14 dígitos) para buscar.");
      return;
    }
    setCnpjLoading(true);
    try {
      const result = await lookupCnpj(digits);
      if (!result) {
        toast.info("CNPJ não encontrado. Preencha os dados manualmente.");
        return;
      }
      if (!data.name)      onChange("name",      result.companyName);
      if (!data.tradeName) onChange("tradeName", result.tradeName ?? "");
      toast.success("Dados da empresa preenchidos automaticamente.");
    } catch {
      toast.error("Consulta de CNPJ indisponível. Preencha manualmente.");
    } finally {
      setCnpjLoading(false);
    }
  };

  return (
    <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-5">
      <div className="space-y-1.5">
        <Label>Tipo de pessoa *</Label>
        <Select
          value={data.personType}
          onValueChange={(v) => onChange("personType", v)}
          disabled={isEdit}
        >
          <SelectTrigger><SelectValue /></SelectTrigger>
          <SelectContent>
            <SelectItem value="Individual">Pessoa física</SelectItem>
            <SelectItem value="Company">Pessoa jurídica</SelectItem>
          </SelectContent>
        </Select>
      </div>

      <div className="space-y-1.5">
        <Label>{isCompany ? "Razão social *" : "Nome completo *"}</Label>
        <Input
          value={data.name}
          onChange={(e) => onChange("name", e.target.value)}
          placeholder={isCompany ? "Razão social da empresa" : "Nome completo do cliente"}
        />
      </div>

      {isCompany && (
        <div className="space-y-1.5">
          <Label>Nome fantasia</Label>
          <Input
            value={data.tradeName}
            onChange={(e) => onChange("tradeName", e.target.value)}
            placeholder="Nome fantasia"
          />
        </div>
      )}

      <div className="space-y-1.5">
        <Label>Tipo de documento *</Label>
        <Select
          value={data.documentType}
          onValueChange={(v) => onChange("documentType", v)}
          disabled={isEdit}
        >
          <SelectTrigger><SelectValue /></SelectTrigger>
          <SelectContent>
            <SelectItem value="CPF">CPF</SelectItem>
            <SelectItem value="CNPJ">CNPJ</SelectItem>
            <SelectItem value="Other">Outro</SelectItem>
          </SelectContent>
        </Select>
      </div>

      <div className="space-y-1.5">
        <Label>Número do documento *</Label>
        <div className="flex gap-2">
          <Input
            value={data.documentNumber}
            onChange={(e) => onChange("documentNumber", e.target.value)}
            placeholder={
              data.documentType === "CPF" ? "000.000.000-00" :
              data.documentType === "CNPJ" ? "00.000.000/0000-00" : ""
            }
            disabled={isEdit}
            className="flex-1"
          />
          {data.documentType === "CNPJ" && !isEdit && (
            <Button
              type="button"
              variant="outline"
              size="icon"
              onClick={handleCnpjLookup}
              disabled={cnpjLoading}
              title="Buscar dados pelo CNPJ"
            >
              {cnpjLoading ? <Loader2 className="h-4 w-4 animate-spin" /> : <Search className="h-4 w-4" />}
            </Button>
          )}
        </div>
      </div>

      <div className="space-y-1.5 flex items-end pb-0.5">
        <div className="flex items-center gap-2">
          <Switch
            checked={data.isActive}
            onCheckedChange={(v) => onChange("isActive", v)}
          />
          <Label>Ativo</Label>
        </div>
      </div>
    </div>
  );
}
