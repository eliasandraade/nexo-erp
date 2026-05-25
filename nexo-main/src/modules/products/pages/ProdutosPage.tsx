import { useState, useRef, useEffect } from "react";
import { useNavigate } from "react-router-dom";
import { Plus, Package, AlertCircle, Tags } from "lucide-react";
import { Button } from "@/components/ui/button";
import { PageHeader } from "@/components/shared/PageHeader";
import { SectionCard } from "@/components/shared/SectionCard";
import { EmptyState } from "@/components/shared/EmptyState";
import { Skeleton } from "@/components/ui/skeleton";
import { DataPagination } from "@/components/shared/DataPagination";
import { ProductFilters } from "../components/ProductFilters";
import { ProductTable } from "../components/ProductTable";
import { ManageCategoriesDialog } from "../components/ManageCategoriesDialog";
import { useProductsPaged, useCategories } from "../hooks/use-products";

export default function ProdutosPage() {
  const navigate = useNavigate();
  const [search, setSearch]           = useState("");
  const [debouncedSearch, setDebouncedSearch] = useState("");
  const [categoryId, setCategoryId]   = useState("all");
  const [status, setStatus]           = useState("all");
  const [unit, setUnit]               = useState("all");
  const [page, setPage]               = useState(1);
  const [manageCatsOpen, setManageCatsOpen] = useState(false);
  const debounceRef = useRef<ReturnType<typeof setTimeout> | null>(null);

  useEffect(() => {
    if (debounceRef.current) clearTimeout(debounceRef.current);
    debounceRef.current = setTimeout(() => {
      setDebouncedSearch(search);
      setPage(1);
    }, 300);
    return () => { if (debounceRef.current) clearTimeout(debounceRef.current); };
  }, [search]);

  const { data, isLoading, isError } = useProductsPaged({
    page,
    pageSize: 50,
    search:          debouncedSearch || undefined,
    includeInactive: status === "inactive" ? true : status === "all" ? false : false,
    isIngredient:    false,
    categoryId:      categoryId !== "all" ? categoryId : undefined,
    unit:            unit !== "all" ? unit : undefined,
  });

  // Active/inactive filter: inactive requires includeInactive=true from above;
  // filter active-only client-side on the current page (25-50 items max).
  const products = status === "inactive"
    ? (data?.items ?? []).filter(p => !p.isActive)
    : status === "active"
    ? (data?.items ?? []).filter(p => p.isActive)
    : (data?.items ?? []);

  const { data: categories = [] } = useCategories();

  const totalPages = data?.totalPages ?? 1;
  const totalCount = data?.totalCount ?? 0;

  return (
    <div className="space-y-6">
      <PageHeader
        title="Produtos"
        description="Gerencie o cadastro e as informações comerciais dos produtos."
        actions={
          <div className="flex gap-2">
            <Button variant="outline" onClick={() => setManageCatsOpen(true)}>
              <Tags className="h-4 w-4 mr-1" /> Categorias
            </Button>
            <Button onClick={() => navigate("/produtos/novo")}>
              <Plus className="h-4 w-4 mr-1" /> Novo produto
            </Button>
          </div>
        }
      />

      <SectionCard>
        <div className="space-y-4">
          <ProductFilters
            search={search}           onSearchChange={(v) => { setSearch(v); }}
            categoryId={categoryId}   onCategoryChange={(v) => { setCategoryId(v); setPage(1); }}
            status={status}           onStatusChange={(v) => { setStatus(v); setPage(1); }}
            unit={unit}               onUnitChange={(v) => { setUnit(v); setPage(1); }}
            categories={categories}
          />

          {isLoading ? (
            <div className="space-y-3">
              {Array.from({ length: 5 }).map((_, i) => (
                <Skeleton key={i} className="h-12 w-full" />
              ))}
            </div>
          ) : isError ? (
            <EmptyState
              icon={AlertCircle}
              title="Erro ao carregar produtos"
              description="Não foi possível carregar a lista de produtos. Tente novamente mais tarde."
            />
          ) : products.length > 0 ? (
            <>
              <ProductTable products={products} categories={categories} />
              <div className="flex items-center justify-between pt-2 text-xs text-muted-foreground">
                <span>{totalCount} produto(s) encontrado(s)</span>
                <DataPagination page={page} totalPages={totalPages} onPageChange={setPage} />
              </div>
            </>
          ) : (
            <EmptyState
              icon={Package}
              title="Nenhum produto encontrado"
              description="Tente ajustar os filtros ou adicione um novo produto."
            />
          )}
        </div>
      </SectionCard>

      <ManageCategoriesDialog
        open={manageCatsOpen}
        onClose={() => setManageCatsOpen(false)}
      />
    </div>
  );
}
