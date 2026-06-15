import { useState } from "react";
import { Loader2, Search } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { lookupCep } from "@/services/integrations.api";
import type { SupplierFormInput } from "../types";

const STATES = [
  "AC","AL","AP","AM","BA","CE","DF","ES","GO","MA",
  "MT","MS","MG","PA","PB","PR","PE","PI","RJ","RN",
  "RS","RO","RR","SC","SP","SE","TO",
];

interface Props {
  data: SupplierFormInput;
  onChange: (field: keyof SupplierFormInput, value: unknown) => void;
}

export function SupplierContactSection({ data, onChange }: Props) {
  const [cepLoading, setCepLoading] = useState(false);

  const handleCepLookup = async () => {
    const digits = data.zipCode.replace(/\D/g, "");
    if (digits.length !== 8) {
      toast.error("Digite um CEP com 8 dígitos para buscar.");
      return;
    }
    setCepLoading(true);
    try {
      const result = await lookupCep(digits);
      if (!result) {
        toast.info("Não encontramos dados para este CEP. Preencha manualmente.");
        return;
      }
      if (!data.street)       onChange("street",       result.street);
      if (!data.neighborhood) onChange("neighborhood", result.neighborhood);
      if (!data.city)         onChange("city",         result.city);
      if (!data.state)        onChange("state",        result.state);
      toast.success("Endereço preenchido automaticamente.");
    } catch {
      toast.error("Consulta de CEP indisponível. Preencha manualmente.");
    } finally {
      setCepLoading(false);
    }
  };

  return (
    <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-5">
      <div className="space-y-1.5">
        <Label>Contato principal</Label>
        <Input
          value={data.contactName}
          onChange={(e) => onChange("contactName", e.target.value)}
          placeholder="Nome do responsável comercial"
        />
      </div>

      <div className="space-y-1.5">
        <Label>Telefone</Label>
        <Input
          value={data.phone}
          onChange={(e) => onChange("phone", e.target.value)}
          placeholder="(00) 00000-0000"
        />
      </div>

      <div className="space-y-1.5 md:col-span-2">
        <Label>E-mail</Label>
        <Input
          type="email"
          value={data.email}
          onChange={(e) => onChange("email", e.target.value)}
          placeholder="contato@fornecedor.com.br"
        />
      </div>

      <div className="space-y-1.5">
        <Label>CEP</Label>
        <div className="flex gap-2">
          <Input
            value={data.zipCode}
            onChange={(e) => onChange("zipCode", e.target.value)}
            placeholder="00000-000"
            className="flex-1"
          />
          <Button
            type="button"
            variant="outline"
            size="icon"
            onClick={handleCepLookup}
            disabled={cepLoading}
            title="Buscar endereço pelo CEP"
          >
            {cepLoading ? <Loader2 className="h-4 w-4 animate-spin" /> : <Search className="h-4 w-4" />}
          </Button>
        </div>
      </div>

      <div className="space-y-1.5 md:col-span-2">
        <Label>Rua / Avenida</Label>
        <Input
          value={data.street}
          onChange={(e) => onChange("street", e.target.value)}
          placeholder="Rua, Avenida..."
        />
      </div>

      <div className="space-y-1.5">
        <Label>Número</Label>
        <Input
          value={data.number}
          onChange={(e) => onChange("number", e.target.value)}
          placeholder="Nº"
        />
      </div>

      <div className="space-y-1.5">
        <Label>Complemento</Label>
        <Input
          value={data.complement}
          onChange={(e) => onChange("complement", e.target.value)}
          placeholder="Sala, Bloco, Galpão..."
        />
      </div>

      <div className="space-y-1.5">
        <Label>Bairro</Label>
        <Input
          value={data.neighborhood}
          onChange={(e) => onChange("neighborhood", e.target.value)}
          placeholder="Bairro"
        />
      </div>

      <div className="space-y-1.5">
        <Label>Cidade</Label>
        <Input
          value={data.city}
          onChange={(e) => onChange("city", e.target.value)}
          placeholder="Cidade"
        />
      </div>

      <div className="space-y-1.5">
        <Label>Estado</Label>
        <select
          value={data.state}
          onChange={(e) => onChange("state", e.target.value)}
          className="flex h-10 w-full rounded-md border border-input bg-background px-3 py-2 text-sm ring-offset-background focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2"
        >
          <option value="">Selecione</option>
          {STATES.map((uf) => (
            <option key={uf} value={uf}>{uf}</option>
          ))}
        </select>
      </div>
    </div>
  );
}
