import { SectionCard } from "@/components/shared/SectionCard";
import { StatusBadge } from "@/components/shared/StatusBadge";
import type { CustomerDto } from "../types";
import { parseAddress } from "../types";

interface Props {
  customer: CustomerDto | null;
}

function formatDate(d: string) {
  return new Date(d).toLocaleDateString("pt-BR", { day: "2-digit", month: "2-digit", year: "numeric", hour: "2-digit", minute: "2-digit" });
}

export function CustomerSummaryCard({ customer }: Props) {
  if (!customer) return null;

  const addr = parseAddress(customer.addressJson);
  const cityDisplay = addr.city ? `${addr.city}${addr.state ? `/${addr.state}` : ""}` : "—";

  const items = [
    { label: "Criado em", value: formatDate(customer.createdAt) },
    { label: "Última atualização", value: formatDate(customer.updatedAt) },
    { label: "Tipo", value: customer.personType === "Individual" ? "Pessoa física" : "Pessoa jurídica" },
    { label: "Documento", value: `${customer.documentType}: ${customer.documentNumber}` },
    { label: "Cidade", value: cityDisplay },
  ];

  return (
    <SectionCard title="Resumo">
      <div className="space-y-3">
        {items.map((i) => (
          <div key={i.label} className="flex justify-between text-sm">
            <span className="text-muted-foreground">{i.label}</span>
            <span className="font-medium text-foreground">{i.value}</span>
          </div>
        ))}
        <div className="flex justify-between text-sm items-center">
          <span className="text-muted-foreground">Status</span>
          <StatusBadge
            label={customer.isActive ? "Ativo" : "Inativo"}
            variant={customer.isActive ? "success" : "neutral"}
          />
        </div>
      </div>
    </SectionCard>
  );
}
