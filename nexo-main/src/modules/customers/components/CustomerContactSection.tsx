import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import type { CustomerFormInput } from "../types";

const STATES = ["AC","AL","AP","AM","BA","CE","DF","ES","GO","MA","MT","MS","MG","PA","PB","PR","PE","PI","RJ","RN","RS","RO","RR","SC","SP","SE","TO"];

interface Props {
  data: CustomerFormInput;
  onChange: (field: keyof CustomerFormInput, value: unknown) => void;
}

export function CustomerContactSection({ data, onChange }: Props) {
  return (
    <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-5">
      <div className="space-y-1.5">
        <Label>E-mail</Label>
        <Input type="email" value={data.email} onChange={(e) => onChange("email", e.target.value)} placeholder="cliente@email.com" />
      </div>
      <div className="space-y-1.5">
        <Label>Telefone</Label>
        <Input value={data.phone} onChange={(e) => onChange("phone", e.target.value)} placeholder="(00) 00000-0000" />
      </div>
      <div className="space-y-1.5">
        <Label>WhatsApp</Label>
        <Input value={data.whatsApp} onChange={(e) => onChange("whatsApp", e.target.value)} placeholder="(00) 00000-0000" />
      </div>
      <div className="space-y-1.5">
        <Label>CEP</Label>
        <Input value={data.zipCode} onChange={(e) => onChange("zipCode", e.target.value)} placeholder="00000-000" />
      </div>
      <div className="space-y-1.5 md:col-span-2">
        <Label>Rua / Avenida</Label>
        <Input value={data.street} onChange={(e) => onChange("street", e.target.value)} placeholder="Rua, Avenida..." />
      </div>
      <div className="space-y-1.5">
        <Label>Número</Label>
        <Input value={data.number} onChange={(e) => onChange("number", e.target.value)} placeholder="Nº" />
      </div>
      <div className="space-y-1.5">
        <Label>Complemento</Label>
        <Input value={data.complement} onChange={(e) => onChange("complement", e.target.value)} placeholder="Apto, Sala..." />
      </div>
      <div className="space-y-1.5">
        <Label>Bairro</Label>
        <Input value={data.neighborhood} onChange={(e) => onChange("neighborhood", e.target.value)} placeholder="Bairro" />
      </div>
      <div className="space-y-1.5">
        <Label>Cidade</Label>
        <Input value={data.city} onChange={(e) => onChange("city", e.target.value)} placeholder="Cidade" />
      </div>
      <div className="space-y-1.5">
        <Label>Estado</Label>
        <select
          value={data.state}
          onChange={(e) => onChange("state", e.target.value)}
          className="flex h-10 w-full rounded-md border border-input bg-background px-3 py-2 text-sm ring-offset-background focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring focus-visible:ring-offset-2"
        >
          <option value="">Selecione</option>
          {STATES.map((s) => <option key={s} value={s}>{s}</option>)}
        </select>
      </div>
    </div>
  );
}
