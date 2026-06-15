import { useState, useEffect } from "react";
import { useParams, useNavigate, useLocation } from "react-router-dom";
import { ChefHat, Package, FileDown, Loader2 } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Label } from "@/components/ui/label";
import { Switch } from "@/components/ui/switch";
import { PageHeader } from "@/components/shared/PageHeader";
import { SectionCard } from "@/components/shared/SectionCard";
import { Skeleton } from "@/components/ui/skeleton";
import { ProductForm } from "../components/ProductForm";
import { IngredientPriceSection } from "../components/IngredientPriceSection";
import { emptyProduct, dtoToProduct } from "../types";
import type { Product } from "../types";
import { useProduct, useCategories, useCreateProduct, useUpdateProduct, useSetProductActive } from "../hooks/use-products";
import { useAuth } from "@/modules/auth/context/AuthContext";
import { toast } from "sonner";
import { downloadPdf } from "@/services/pdf.api";

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
  const location = useLocation();
  const { session } = useAuth();
  const isNew = !id;
  const hasRestauranteModule = session?.modules?.includes("restaurante") ?? false;

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

  // Pre-set isIngredient when URL has ?tipo=ingrediente
  useEffect(() => {
    if (isNew && location.search.includes("tipo=ingrediente")) {
      setFormData((prev) => ({ ...prev, isIngredient: true }));
    }
  }, [isNew, location.search]);

  const [pdfLoading, setPdfLoading] = useState(false);

  const handleDownloadSheet = async () => {
    if (!id) return;
    setPdfLoading(true);
    try {
      await downloadPdf(`/products/${id}/sheet.pdf`, `ficha-produto.pdf`);
    } catch {
      toast.error("Falha ao gerar PDF. Tente novamente.");
    } finally {
      setPdfLoading(false);
    }
  };

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
      isIngredient:     formData.isIngredient ?? false,
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
            {!isNew && (
              <Button
                variant="outline"
                size="sm"
                onClick={handleDownloadSheet}
                disabled={pdfLoading || !id}
              >
                {pdfLoading ? (
                  <Loader2 className="mr-2 h-4 w-4 animate-spin" />
                ) : (
                  <FileDown className="mr-2 h-4 w-4" />
                )}
                Baixar ficha
              </Button>
            )}
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
        {/* Toggle ingrediente / cardápio */}
        <div className="flex items-center gap-4 pb-4 border-b mb-4">
          <div className="flex items-center gap-2">
            <Package className="h-4 w-4 text-muted-foreground" />
            <Label className="text-sm font-medium">Item do cardápio</Label>
          </div>
          <Switch
            checked={formData.isIngredient ?? false}
            onCheckedChange={(v) => setFormData((prev) => ({ ...prev, isIngredient: v }))}
          />
          <div className="flex items-center gap-2">
            <ChefHat className="h-4 w-4 text-muted-foreground" />
            <Label className="text-sm font-medium">Ingrediente de estoque</Label>
          </div>
        </div>

        <ProductForm
          data={formData}
          onChange={setFormData}
          isNew={isNew}
          categories={categories}
        />

        {/* Seção de preços de compra — apenas para ingredientes salvos */}
        {!isNew && formData.isIngredient && id && (
          <div className="mt-6 pt-6 border-t space-y-3">
            <h3 className="text-sm font-semibold">Histórico de preços de compra</h3>
            <IngredientPriceSection productId={id} />
          </div>
        )}

        {/* Link para ficha técnica — apenas para cardápio com restaurante */}
        {!isNew && !formData.isIngredient && hasRestauranteModule && id && (
          <div className="mt-6 pt-6 border-t flex items-center justify-between">
            <div>
              <h3 className="text-sm font-semibold">Ficha Técnica</h3>
              <p className="text-xs text-muted-foreground">CMV, ingredientes e modo de preparo.</p>
            </div>
            <Button variant="outline" onClick={() => navigate(`/produtos/${id}/ficha`)}>
              <ChefHat className="h-4 w-4 mr-2" />
              Abrir Ficha Técnica
            </Button>
          </div>
        )}
      </SectionCard>
    </div>
  );
}
