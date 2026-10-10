import { useState, useEffect, useRef } from "react";
import { useNavigate } from "react-router-dom";
import { PageHeader } from "@/components/shared/PageHeader";
import { ErrorState } from "@/components/shared/ErrorState";
import { EmptyState } from "@/components/shared/EmptyState";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { Plus } from "lucide-react";
import { DataPagination } from "@/components/shared/DataPagination";
import { CustomerFilters } from "../components/CustomerFilters";
import { CustomerTable } from "../components/CustomerTable";
import { useCustomersList } from "../hooks/useCustomersList";

const PAGE_SIZE = 25;

export default function ClientesPage() {
  const navigate = useNavigate();

  const [search, setSearch]         = useState("");
  const [personType, setPersonType] = useState("all");
  const [isActive, setIsActive]     = useState("all");
  const [page, setPage]             = useState(1);
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

  const { data, isLoading, isError, isFetching, refetch } = useCustomersList({
    page,
    pageSize: PAGE_SIZE,
    search:          debouncedSearch || undefined,
    includeInactive: isActive === "all" || isActive === "false",
  });

  const allItems   = data?.items ?? [];
  const totalPages = data?.totalPages ?? 1;
  const totalCount = data?.totalCount ?? 0;

  const customers = personType === "all"
    ? allItems
    : allItems.filter((c) => c.personType === personType);

  const filtered = isActive === "false"
    ? customers.filter((c) => !c.isActive)
    : isActive === "true"
    ? customers.filter((c) => c.isActive)
    : customers;

  return (
    <div className="space-y-6">
      <PageHeader
        title="Clientes"
        eyebrow="Cadastros"
        actions={
          <Button onClick={() => navigate("/clientes/novo")}>
            <Plus className="h-4 w-4 mr-2" /> Novo cliente
          </Button>
        }
      />

      <section className="data-region p-4 sm:p-6" aria-label="Lista de clientes" aria-busy={isFetching}>
        <div className="space-y-4">
          <CustomerFilters
            search={search} onSearchChange={(v) => { setSearch(v); }}
            personType={personType} onPersonTypeChange={(v) => { setPersonType(v); setPage(1); }}
            isActive={isActive} onIsActiveChange={(v) => { setIsActive(v); setPage(1); }}
          />

          {isLoading && (
            <div className="space-y-2">
              {[1, 2, 3].map((i) => <Skeleton key={i} className="h-12 w-full" />)}
            </div>
          )}

          {isError && (
            <ErrorState title="Não foi possível carregar os clientes" description="Seus cadastros não foram alterados." onRetry={() => void refetch()} />
          )}

          {!isLoading && !isError && filtered.length === 0 && (
            <EmptyState
              title={search || personType !== "all" || isActive !== "all" ? "Nenhum cliente nesta seleção" : "Nenhum cliente cadastrado"}
              description={search ? "Tente outro nome, documento ou contato." : personType !== "all" || isActive !== "all" ? "Os filtros de tipo e situação se aplicam à página atual." : "Cadastre os dados de contato para usar nas vendas e nos atendimentos."}
              action={search || personType !== "all" || isActive !== "all" ? <Button variant="outline" onClick={() => { setSearch(""); setPersonType("all"); setIsActive("all"); setPage(1); }}>Limpar filtros</Button> :
                <Button variant="outline" onClick={() => navigate("/clientes/novo")}>
                  <Plus className="h-4 w-4 mr-2" /> Cadastrar cliente
                </Button>
              }
            />
          )}

          {!isLoading && !isError && filtered.length > 0 && (
            <>
              <p className="text-xs text-muted-foreground" role="status">{filtered.length} nesta página · {totalCount} no resultado da busca{isFetching ? " · Atualizando…" : ""}</p>
              <CustomerTable customers={filtered} />

            </>
          )}
          {!isLoading && !isError && totalPages > 1 && <DataPagination page={page} totalPages={totalPages} onPageChange={setPage} />}
        </div>
      </section>
    </div>
  );
}
