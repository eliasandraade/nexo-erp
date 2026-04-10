import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Select, SelectContent, SelectItem, SelectTrigger, SelectValue,
} from "@/components/ui/select";
import { Switch } from "@/components/ui/switch";
import type { CategoryDto, Product } from "../types";
import { productUnitLabels } from "../types";

interface Props {
  data: Partial<Product>;
  onChange: (field: string, value: unknown) => void;
  categories: CategoryDto[];
}

export function ProductMainDataSection({ data, onChange, categories }: Props) {
  return (
    <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-5">
      <div className="space-y-1.5">
        <Label>Código interno</Label>
        <Input
          value={data.code ?? ""}
          onChange={(e) => onChange("code", e.target.value)}
          placeholder="PRD-000"
        />
      </div>
      <div className="space-y-1.5">
        <Label>Código de barras</Label>
        <Input
          value={data.barcode ?? ""}
          onChange={(e) => onChange("barcode", e.target.value)}
          placeholder="EAN-13"
        />
      </div>
      <div className="space-y-1.5 md:col-span-2 lg:col-span-1">
        <Label>Nome do produto</Label>
        <Input
          value={data.name ?? ""}
          onChange={(e) => onChange("name", e.target.value)}
          placeholder="Nome do produto"
        />
      </div>
      <div className="space-y-1.5 md:col-span-2 lg:col-span-3">
        <Label>Descrição adicional</Label>
        <Input
          value={data.description ?? ""}
          onChange={(e) => onChange("description", e.target.value)}
          placeholder="Informações complementares sobre o produto"
        />
      </div>
      <div className="space-y-1.5">
        <Label>Categoria</Label>
        <Select
          value={data.categoryId ?? "none"}
          onValueChange={(v) => onChange("categoryId", v === "none" ? null : v)}
        >
          <SelectTrigger>
            <SelectValue placeholder="Selecione" />
          </SelectTrigger>
          <SelectContent>
            <SelectItem value="none">Sem categoria</SelectItem>
            {categories.map((c) => (
              <SelectItem key={c.id} value={c.id}>{c.name}</SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>
      <div className="space-y-1.5">
        <Label>Unidade de medida</Label>
        <Select
          value={data.unit ?? "Un"}
          onValueChange={(v) => onChange("unit", v)}
        >
          <SelectTrigger><SelectValue /></SelectTrigger>
          <SelectContent>
            {Object.entries(productUnitLabels).map(([value, label]) => (
              <SelectItem key={value} value={value}>{label}</SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>
      <div className="space-y-1.5">
        <Label>Controle de estoque</Label>
        <div className="flex items-center gap-3 pt-2">
          <Switch
            checked={data.trackStock ?? true}
            onCheckedChange={(v) => onChange("trackStock", v)}
          />
          <span className="text-sm text-muted-foreground">
            {data.trackStock ? "Ativo" : "Inativo"}
          </span>
        </div>
      </div>
      <div className="flex items-center gap-3 pt-5">
        <Switch
          checked={data.isActive ?? true}
          onCheckedChange={(v) => onChange("isActive", v)}
        />
        <Label>Produto ativo</Label>
      </div>
    </div>
  );
}
