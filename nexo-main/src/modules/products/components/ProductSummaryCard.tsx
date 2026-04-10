import { SectionCard } from "@/components/shared/SectionCard";
import { StatusBadge } from "@/components/shared/StatusBadge";
import { format } from "date-fns";
import type { CategoryDto, Product } from "../types";

interface Props {
  data: Partial<Product>;
  isNew?: boolean;
  categories?: CategoryDto[];
}

export function ProductSummaryCard({ data, isNew, categories = [] }: Props) {
  const categoryName = data.categoryId
    ? (categories.find((c) => c.id === data.categoryId)?.name ?? "—")
    : "—";

  const margin =
    data.salePrice && data.costPrice && data.salePrice > 0
      ? (((data.salePrice - data.costPrice) / data.salePrice) * 100).toFixed(1)
      : null;

  return (
    <div className="grid grid-cols-1 lg:grid-cols-2 gap-5">
      <SectionCard title="Informações do registro">
        <div className="space-y-3 text-sm">
          <div className="flex justify-between">
            <span className="text-muted-foreground">Criado em</span>
            <span>
              {isNew || !data.createdAt
                ? "—"
                : format(new Date(data.createdAt), "dd/MM/yyyy HH:mm")}
            </span>
          </div>
          <div className="flex justify-between">
            <span className="text-muted-foreground">Última atualização</span>
            <span>
              {data.updatedAt
                ? format(new Date(data.updatedAt), "dd/MM/yyyy HH:mm")
                : "—"}
            </span>
          </div>
          <div className="flex justify-between items-center">
            <span className="text-muted-foreground">Status</span>
            <StatusBadge
              label={data.isActive ? "Ativo" : "Inativo"}
              variant={data.isActive ? "success" : "neutral"}
            />
          </div>
          <div className="flex justify-between">
            <span className="text-muted-foreground">Categoria</span>
            <span className="font-medium">{categoryName}</span>
          </div>
        </div>
      </SectionCard>

      <SectionCard title="Indicadores rápidos">
        <div className="space-y-3 text-sm">
          <div className="flex justify-between">
            <span className="text-muted-foreground">Preço de venda</span>
            <span className="font-medium">
              {data.salePrice?.toLocaleString("pt-BR", { style: "currency", currency: "BRL" }) ?? "—"}
            </span>
          </div>
          <div className="flex justify-between">
            <span className="text-muted-foreground">Custo</span>
            <span>
              {data.costPrice?.toLocaleString("pt-BR", { style: "currency", currency: "BRL" }) ?? "—"}
            </span>
          </div>
          <div className="flex justify-between">
            <span className="text-muted-foreground">Margem</span>
            <span className="font-medium">{margin !== null ? `${margin}%` : "—"}</span>
          </div>
          <div className="flex justify-between">
            <span className="text-muted-foreground">Estoque mínimo</span>
            <span>{data.minStockQuantity ?? "—"}</span>
          </div>
        </div>
      </SectionCard>

      <SectionCard title="Histórico e auditoria" className="lg:col-span-2">
        <p className="text-sm text-muted-foreground">
          Área reservada para futura integração com histórico de alterações e log de auditoria do produto.
        </p>
      </SectionCard>
    </div>
  );
}
