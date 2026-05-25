import { useState, useEffect, useRef } from "react";
import { useNavigate } from "react-router-dom";
import { Plus, Truck } from "lucide-react";
import { PageHeader } from "@/components/shared/PageHeader";
import { SectionCard } from "@/components/shared/SectionCard";
import { EmptyState } from "@/components/shared/EmptyState";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { DataPagination } from "@/components/shared/DataPagination";
import { SupplierFilters } from "../components/SupplierFilters";
import { SupplierTable } from "../components/SupplierTable";
import { useSuppliersList } from "../hooks/useSuppliersList";

const PAGE_SIZE = 25;

export default function FornecedoresPage() {
  const navigate = useNavigate();

  const [search, setSearch]     = useState("");
  const [isActive, setIsActive] = useState("all");
  const [page, setPage]         = useState(1);
  const [debouncedSearch, setDebouncedSearch] = useState("");
  const debounceRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  useEffect(() => {
    if (debounceRef.current) clearTimeout(debounceRef.current);
    debounceRef.current = setTimeout(() => {
      setDebouncedSearch(search);
      setPage(1);
    }, 300);
    return () => { if (debounceRef.current) clearTimeout(debounceRef.current); };
  }, [search]);

  const { data, isLoading, isError } = useSuppliersList({
    page,
    pageSize: PAGE_SIZE,
    search:          debouncedSearch || undefined,
    includeInactive: isActive === "all" || isActive === "false",
  });

  const allItems   = data?.items ?? [];
  const totalPages = data?.totalPages ?? 1;
  const totalCount = data?.totalCount ?? 0;

  const suppliers = isActive === "false"
    ? allItems.filter((s) => !s.isActive)
    : isActive === "true"
    ? allItems.filter((s) => s.isActive)
    : allItems;

  const hasFilters = !!debouncedSearch || isActive !== "all";

  return (
    <div className="space-y-6">
      <PageHeader
        title="Fornecedores"
        description="Gerencie os cadastros e dados comerciais dos fornecedores."
        actions={
          <Button onClick={() => navigate("/fornecedores/novo")}>
            <Plus className="h-4 w-4 mr-2" />
            Novo fornecedor
          </Button>
        }
      />

      <SectionCard>
        <div className="space-y-4">
          <SupplierFilters
            search={search} onSearchChange={(v) => { setSearch(v); }}
            isActive={isActive} onIsActiveChange={(v) => { setIsActive(v); setPage(1); }}
          />

          {isLoading && (
            <div className="space-y-2">
              {[1, 2, 3, 4].map((i) => (
                <Skeleton key={i} className="h-12 w-full" />
              ))}
            </div>
          )}

          {isError && (
            <EmptyState
              icon={Truck}
              title="Erro ao carregar fornecedores"
              description="Não foi possível buscar os dados. Tente novamente mais tarde."
            />
          )}

          {!isLoading && !isError && suppliers.length === 0 && (
            <EmptyState
              icon={Truck}
              title={hasFilters ? "Nenhum fornecedor encontrado" : "Nenhum fornecedor cadastrado"}
              description={hasFilters
                ? "Tente ajustar os filtros da busca."
                : "Cadastre fornecedores para gerenciar compras e reposição de estoque."}
              action={!hasFilters ? (
                <Button variant="outline" onClick={() => navigate("/fornecedores/novo")}>
                  <Plus className="h-4 w-4 mr-2" />
                  Cadastrar fornecedor
                </Button>
              ) : undefined}
            />
          )}

          {!isLoading && !isError && suppliers.length > 0 && (
            <>
              <p className="text-xs text-muted-foreground">
                {totalCount} fornecedor(es) encontrado(s)
              </p>
              <SupplierTable suppliers={suppliers} />
              <DataPagination page={page} totalPages={totalPages} onPageChange={setPage} />
            </>
          )}
        </div>
      </SectionCard>
    </div>
  );
}
