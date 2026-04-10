import { useState, useEffect } from "react";
import { useParams, useNavigate } from "react-router-dom";
import { Button } from "@/components/ui/button";
import { PageHeader } from "@/components/shared/PageHeader";
import { SectionCard } from "@/components/shared/SectionCard";
import { Skeleton } from "@/components/ui/skeleton";
import { ProductForm } from "../components/ProductForm";
import { emptyProduct, dtoToProduct } from "../types";
import type { Product } from "../types";
import { useProduct, useCategories, useCreateProduct, useUpdateProduct, useSetProductActive } from "../hooks/use-products";
import { toast } from "sonner";

function validate(data: Partial<Product>): string[] {
  const errors: string[] = [];
  if (!data.code?.trim())  errors.push("Código é obrigatório.");
  if (!data.name?.trim())  errors.push("Nome é obrigatório.");
  if (!data.unit)          errors.push("Unidade é obrigatória.");
  if (data.salePrice == null || data.salePrice < 0)
    errors.push("Preço de venda deve ser >= 0.");
  if (data.costPrice != null && data.costPrice < 0)
    errors.push("Custo deve ser >= 0.");
  return errors;
}

export default function ProductFormPage() {
  const { id } = useParams();
  const navigate = useNavigate();
  const isNew = !id;

  const { data: dto, isLoading: loadingProduct } = useProduct(id);
  const { data: categories = [] } = useCategories();
  const createMutation = useCreateProduct();
  const updateMutation = useUpdateProduct(id ?? "");
  const setActiveMutation = useSetProductActive(id ?? "");

  const [formData, setFormData] = useState<Partial<Product>>(emptyProduct);

  // Populate form once the DTO arrives
  useEffect(() => {
    if (dto) setFormData(dtoToProduct(dto));
  }, [dto]);

  const saving =
    createMutation.isPending ||
    updateMutation.isPending ||
    setActiveMutation.isPending;

  const handleSave = async () => {
    const errors = validate(formData);
    if (errors.length > 0) {
      toast.error("Verifique os campos", { description: errors.join(" ") });
      return;
    }

    const payload = {
      name:             formData.name!,
      unit:             formData.unit!,
      salePrice:        formData.salePrice ?? 0,
      costPrice:        formData.costPrice ?? 0,
      trackStock:       formData.trackStock ?? true,
      barcode:          formData.barcode || null,
      description:      formData.description || null,
      categoryId:       formData.categoryId || null,
      minStockQuantity: formData.minStockQuantity || null,
      maxStockQuantity: formData.maxStockQuantity || null,
    };

    try {
      if (isNew) {
        await createMutation.mutateAsync({ code: formData.code!, ...payload });
        toast.success("Produto criado com sucesso.");
      } else {
        const updated = await updateMutation.mutateAsync(payload);
        // Handle isActive toggle separately (activate/deactivate endpoint)
        if (dto && updated.isActive !== (formData.isActive ?? true)) {
          await setActiveMutation.mutateAsync(formData.isActive ?? true);
        }
        toast.success("Produto atualizado com sucesso.");
      }
      navigate("/produtos");
    } catch (err: unknown) {
      const message = err instanceof Error ? err.message : "Erro desconhecido.";
      toast.error("Erro ao salvar produto", { description: message });
    }
  };

  if (!isNew && loadingProduct) {
    return (
      <div className="space-y-6">
        <Skeleton className="h-16 w-full" />
        <Skeleton className="h-64 w-full" />
      </div>
    );
  }

  return (
    <div className="space-y-6">
      <PageHeader
        title={isNew ? "Novo produto" : "Editar produto"}
        description="Cadastre e configure as informações do produto."
        actions={
          <div className="flex items-center gap-2">
            <Button
              variant="outline"
              onClick={() => navigate("/produtos")}
              disabled={saving}
            >
              Cancelar
            </Button>
            <Button onClick={handleSave} disabled={saving}>
              {saving ? "Salvando…" : "Salvar"}
            </Button>
          </div>
        }
      />
      <SectionCard>
        <ProductForm
          data={formData}
          onChange={setFormData}
          isNew={isNew}
          categories={categories}
        />
      </SectionCard>
    </div>
  );
}
