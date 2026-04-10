import { useState, useMemo } from "react";
import { useNavigate } from "react-router-dom";
import { Plus, Truck } from "lucide-react";
import { PageHeader } from "@/components/shared/PageHeader";
import { SectionCard } from "@/components/shared/SectionCard";
import { EmptyState } from "@/components/shared/EmptyState";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { useSuppliers } from "../hooks/use-suppliers";
import { parseAddress } from "../types";
import { SupplierFilters } from "../components/SupplierFilters";
import { SupplierTable } from "../components/SupplierTable";

export default function FornecedoresPage() {
  const navigate = useNavigate();
  const { data: suppliers, isLoading, isError } = useSuppliers(true);

  const [search, setSearch] = useState("");
  const [isActive, setIsActive] = useState("all");
  const [city, setCity] = useState("all");

  const cities = useMemo(() => {
    if (!suppliers) return [];
    const citySet = new Set<string>();
    for (const s of suppliers) {
      const addr = parseAddress(s.addressJson);
      if (addr.city) citySet.add(addr.city);
    }
    return [...citySet].sort();
  }, [suppliers]);

  const filtered = useMemo(() => {
    if (!suppliers) return [];
    return suppliers.filter((s) => {
      const q = search.toLowerCase();
      const matchSearch =
        !q ||
        s.name.toLowerCase().includes(q) ||
        (s.tradeName ?? "").toLowerCase().includes(q) ||
        s.documentNumber.replace(/\D/g, "").includes(q.replace(/\D/g, "")) ||
        (s.phone ?? "").replace(/\D/g, "").includes(q.replace(/\D/g, "")) ||
        (s.email ?? "").toLowerCase().includes(q) ||
        (s.contactName ?? "").toLowerCase().includes(q);
      const matchActive =
        isActive === "all" || (isActive === "true" ? s.isActive : !s.isActive);
      const addr = parseAddress(s.addressJson);
      const matchCity = city === "all" || addr.city === city;
      return matchSearch && matchActive && matchCity;
    });
  }, [suppliers, search, isActive, city]);

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
            search={search} onSearchChange={setSearch}
            isActive={isActive} onIsActiveChange={setIsActive}
            city={city} onCityChange={setCity}
            cities={cities}
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

          {!isLoading && !isError && filtered.length === 0 && (
            <EmptyState
              icon={Truck}
              title={search || isActive !== "all" || city !== "all"
                ? "Nenhum fornecedor encontrado"
                : "Nenhum fornecedor cadastrado"}
              description={search || isActive !== "all" || city !== "all"
                ? "Tente ajustar os filtros da busca."
                : "Cadastre fornecedores para gerenciar compras e reposição de estoque."}
              action={!search && isActive === "all" && city === "all" ? (
                <Button variant="outline" onClick={() => navigate("/fornecedores/novo")}>
                  <Plus className="h-4 w-4 mr-2" />
                  Cadastrar fornecedor
                </Button>
              ) : undefined}
            />
          )}

          {!isLoading && !isError && filtered.length > 0 && (
            <>
              <p className="text-xs text-muted-foreground">
                {filtered.length} fornecedor(es) encontrado(s)
              </p>
              <SupplierTable suppliers={filtered} />
            </>
          )}
        </div>
      </SectionCard>
    </div>
  );
}
