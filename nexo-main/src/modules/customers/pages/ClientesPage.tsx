import { useState, useEffect, useRef } from "react";
import { useNavigate } from "react-router-dom";
import { PageHeader } from "@/components/shared/PageHeader";
import { SectionCard } from "@/components/shared/SectionCard";
import { EmptyState } from "@/components/shared/EmptyState";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { Plus, Users } from "lucide-react";
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

  const { data, isLoading, isError } = useCustomersList({
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
        description="Gerencie os cadastros e informações comerciais dos clientes."
        actions={
          <Button onClick={() => navigate("/clientes/novo")}>
            <Plus className="h-4 w-4 mr-2" /> Novo cliente
          </Button>
        }
      />

      <SectionCard>
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
            <EmptyState icon={Users} title="Erro ao carregar clientes" description="Tente novamente mais tarde." />
          )}

          {!isLoading && !isError && filtered.length === 0 && (
            <EmptyState
              icon={Users}
              title="Nenhum cliente encontrado"
              description="Adicione clientes para acompanhar vendas e relacionamento."
              action={
                <Button variant="outline" onClick={() => navigate("/clientes/novo")}>
                  <Plus className="h-4 w-4 mr-2" /> Cadastrar cliente
                </Button>
              }
            />
          )}

          {!isLoading && !isError && filtered.length > 0 && (
            <>
              <p className="text-xs text-muted-foreground">{totalCount} cliente(s) encontrado(s)</p>
              <CustomerTable customers={filtered} />
              <DataPagination page={page} totalPages={totalPages} onPageChange={setPage} />
            </>
          )}
        </div>
      </SectionCard>
    </div>
  );
}
