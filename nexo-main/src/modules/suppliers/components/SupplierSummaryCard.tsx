import { SectionCard } from "@/components/shared/SectionCard";
import { StatusBadge } from "@/components/shared/StatusBadge";
import type { SupplierDto } from "../types";
import { parseAddress, parseBankInfo } from "../types";

interface Props {
  supplier: SupplierDto | null;
}

function formatDateTime(iso: string) {
  return new Date(iso).toLocaleString("pt-BR", {
    day: "2-digit", month: "2-digit", year: "numeric",
    hour: "2-digit", minute: "2-digit",
  });
}

export function SupplierSummaryCard({ supplier }: Props) {
  if (!supplier) return null;

  const addr = parseAddress(supplier.addressJson);
  const bank = parseBankInfo(supplier.bankInfoJson);
  const cityDisplay = addr.city
    ? `${addr.city}${addr.state ? `/${addr.state}` : ""}`
    : "—";

  const rows: { label: string; value: React.ReactNode }[] = [
    { label: "Tipo", value: supplier.personType === "Individual" ? "Pessoa física" : "Pessoa jurídica" },
    { label: "Documento", value: `${supplier.documentType}: ${supplier.documentNumber}` },
    { label: "Cidade", value: cityDisplay },
    {
      label: "Prazo padrão",
      value: supplier.paymentTermsDays != null ? `${supplier.paymentTermsDays} dias` : "—",
    },
    ...(bank.pixKey ? [{ label: "Chave Pix", value: bank.pixKey }] : []),
    { label: "Cadastrado em", value: formatDateTime(supplier.createdAt) },
    { label: "Última atualização", value: formatDateTime(supplier.updatedAt) },
  ];

  return (
    <SectionCard title="Resumo">
      <div className="space-y-3">
        {rows.map((row) => (
          <div key={row.label} className="flex items-start justify-between gap-4 text-sm">
            <span className="text-muted-foreground shrink-0">{row.label}</span>
            <span className="font-medium text-foreground text-right">{row.value}</span>
          </div>
        ))}
        <div className="flex items-center justify-between text-sm">
          <span className="text-muted-foreground">Status</span>
          <StatusBadge
            label={supplier.isActive ? "Ativo" : "Inativo"}
            variant={supplier.isActive ? "success" : "neutral"}
          />
        </div>
      </div>
    </SectionCard>
  );
}
