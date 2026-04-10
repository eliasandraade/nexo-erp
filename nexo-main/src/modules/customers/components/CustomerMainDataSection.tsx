import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Switch } from "@/components/ui/switch";
import type { CustomerFormInput } from "../types";

interface Props {
  data: CustomerFormInput;
  onChange: (field: keyof CustomerFormInput, value: unknown) => void;
  isEdit?: boolean;
}

export function CustomerMainDataSection({ data, onChange, isEdit = false }: Props) {
  const isCompany = data.personType === "Company";

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
        <Input
          value={data.documentNumber}
          onChange={(e) => onChange("documentNumber", e.target.value)}
          placeholder={data.documentType === "CPF" ? "000.000.000-00" : data.documentType === "CNPJ" ? "00.000.000/0000-00" : ""}
          disabled={isEdit}
        />
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
