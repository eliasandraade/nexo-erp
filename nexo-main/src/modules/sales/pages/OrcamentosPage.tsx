import { useState } from "react";
import { useNavigate } from "react-router-dom";
import { AlertCircle, FileText, Plus } from "lucide-react";
import { useQuery } from "@tanstack/react-query";
import { PageHeader } from "@/components/shared/PageHeader";
import { SectionCard } from "@/components/shared/SectionCard";
import { EmptyState } from "@/components/shared/EmptyState";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { quotationService } from "../services/quotationService";
import { QuotationFilters } from "../components/QuotationFilters";
import { QuotationTable } from "../components/QuotationTable";
import type { QuotationListFilters } from "../types/quotation";

const defaultFilters: QuotationListFilters = {
  search: "",
  status: "all",
  operator: "all",
};

export default function OrcamentosPage() {
  const navigate = useNavigate();
  const [filters, setFilters] = useState<QuotationListFilters>(defaultFilters);

  const { data: operators = [] } = useQuery({
    queryKey: ["quotation-operators"],
    queryFn: () => quotationService.listOperators(),
    staleTime: 60_000,
  });

  const { data: quotations = [], isLoading, isError } = useQuery({
    queryKey: ["quotations", filters],
    queryFn: () => quotationService.list(filters),
  });

  const hasActiveFilters =
    filters.search !== "" ||
    filters.status !== "all" ||
    filters.operator !== "all";

  return (
    <div className="space-y-6">
      <PageHeader
        title="Orçamentos"
        description="Crie e gerencie orçamentos para seus clientes."
        actions={
          <Button onClick={() => navigate("/orcamentos/novo")} className="gap-2">
            <Plus className="h-4 w-4" />
            Novo orçamento
          </Button>
        }
      />

      <SectionCard>
        <div className="space-y-4">
          <QuotationFilters
            filters={filters}
            operators={operators}
            onChange={setFilters}
          />

          {isLoading ? (
            <div className="space-y-3">
              {Array.from({ length: 6 }).map((_, i) => (
                <Skeleton key={i} className="h-12 w-full" />
              ))}
            </div>
          ) : isError ? (
            <EmptyState
              icon={AlertCircle}
              title="Erro ao carregar orçamentos"
              description="Não foi possível carregar os orçamentos. Tente novamente."
            />
          ) : quotations.length > 0 ? (
            <>
              <QuotationTable quotations={quotations} />
              <div className="pt-2 text-xs text-muted-foreground">
                {quotations.length} orçamento(s) encontrado(s)
              </div>
            </>
          ) : (
            <EmptyState
              icon={FileText}
              title="Nenhum orçamento encontrado"
              description={
                hasActiveFilters
                  ? "Nenhum orçamento corresponde aos filtros selecionados."
                  : "Crie seu primeiro orçamento clicando em \"Novo orçamento\"."
              }
            />
          )}
        </div>
      </SectionCard>
    </div>
  );
}
