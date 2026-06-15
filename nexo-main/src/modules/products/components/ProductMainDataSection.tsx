import { useState } from "react";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Button } from "@/components/ui/button";
import { Card, CardContent } from "@/components/ui/card";
import {
  Select, SelectContent, SelectItem, SelectTrigger, SelectValue,
} from "@/components/ui/select";
import { Switch } from "@/components/ui/switch";
import { toast } from "sonner";
import { Search, Loader2, X } from "lucide-react";
import type { CategoryDto, Product } from "../types";
import { productUnitLabels } from "../types";
import { ImageUploadButton } from "@/components/shared/ImageUploadButton";
import { patchProductImage } from "../api/products.api";
import {
  lookupBarcodeProduct,
  type BarcodeLookupResult,
} from "@/services/integrations.api";

interface Props {
  data: Partial<Product>;
  onChange: (field: string, value: unknown) => void;
  categories: CategoryDto[];
}

type LookupStatus = "idle" | "not_found" | "unavailable";

export function ProductMainDataSection({ data, onChange, categories }: Props) {
  const [barcodeInput, setBarcodeInput] = useState(data.barcode ?? "");
  const [lookupLoading, setLookupLoading] = useState(false);
  const [suggestion, setSuggestion] = useState<BarcodeLookupResult | null>(null);
  const [lookupStatus, setLookupStatus] = useState<LookupStatus>("idle");

  const handleImageChange = async (url: string | null) => {
    onChange("imageUrl", url);
    if (!data.id) return;
    try {
      await patchProductImage(data.id, url);
    } catch {
      toast.error("Falha ao salvar imagem. Tente novamente.");
    }
  };

  const handleBarcodeChange = (value: string) => {
    setBarcodeInput(value);
    onChange("barcode", value);
    // reset suggestion when barcode changes
    setSuggestion(null);
    setLookupStatus("idle");
  };

  const handleLookup = async () => {
    const digits = barcodeInput.replace(/\D/g, "");
    if (digits.length < 8 || digits.length > 14) {
      toast.error("Código de barras inválido. Use entre 8 e 14 dígitos.");
      return;
    }
    setLookupLoading(true);
    setSuggestion(null);
    setLookupStatus("idle");
    try {
      const result = await lookupBarcodeProduct(barcodeInput);
      if (result.unavailable) {
        setLookupStatus("unavailable");
      } else if (result.found && result.data) {
        setSuggestion(result.data);
        setLookupStatus("idle");
      } else {
        setLookupStatus("not_found");
      }
    } catch {
      setLookupStatus("unavailable");
    } finally {
      setLookupLoading(false);
    }
  };

  const handleApplySuggestion = () => {
    if (!suggestion) return;
    if (!data.name) onChange("name", suggestion.name);
    // brand is not a field on Product, skip
    setSuggestion(null);
    setLookupStatus("idle");
    toast.success("Sugestão aplicada.");
  };

  const handleDismiss = () => {
    setSuggestion(null);
    setLookupStatus("idle");
  };

  return (
    <div className="space-y-5">
      {/* Barcode section */}
      <div className="space-y-3">
        <div className="space-y-1.5">
          <Label>Código de barras</Label>
          <div className="flex gap-2">
            <Input
              value={barcodeInput}
              onChange={(e) => handleBarcodeChange(e.target.value)}
              placeholder="EAN-13"
              className="flex-1"
              onKeyDown={(e) => {
                if (e.key === "Enter") handleLookup();
              }}
            />
            <Button
              type="button"
              variant="outline"
              onClick={handleLookup}
              disabled={lookupLoading || barcodeInput.trim() === ""}
              className="shrink-0"
            >
              {lookupLoading ? (
                <Loader2 className="h-4 w-4 animate-spin" />
              ) : (
                <Search className="h-4 w-4" />
              )}
              <span className="ml-2">Buscar por código</span>
            </Button>
          </div>
        </div>

        {/* Suggestion card */}
        {suggestion && (
          <Card className="border-indigo-500/40 bg-indigo-950/30">
            <CardContent className="p-4 space-y-3">
              <div className="flex items-start justify-between gap-3">
                <p className="text-sm font-medium text-indigo-300">
                  Produto encontrado — confirme os dados antes de aplicar
                </p>
                <Button
                  type="button"
                  variant="ghost"
                  size="icon"
                  className="shrink-0 h-6 w-6 text-muted-foreground hover:text-foreground"
                  onClick={handleDismiss}
                >
                  <X className="h-4 w-4" />
                </Button>
              </div>

              <div className="flex gap-4">
                {suggestion.imageUrl && (
                  <img
                    src={suggestion.imageUrl}
                    alt={suggestion.name}
                    className="h-20 w-20 rounded object-contain border border-border bg-white shrink-0"
                  />
                )}
                <div className="space-y-1 text-sm min-w-0">
                  <div>
                    <span className="text-muted-foreground">Nome: </span>
                    <span className="font-medium">{suggestion.name}</span>
                  </div>
                  {suggestion.brand && (
                    <div>
                      <span className="text-muted-foreground">Marca: </span>
                      <span>{suggestion.brand}</span>
                    </div>
                  )}
                  {suggestion.category && (
                    <div>
                      <span className="text-muted-foreground">Categoria: </span>
                      <span>{suggestion.category}</span>
                    </div>
                  )}
                  {(suggestion.quantity || suggestion.unit) && (
                    <div>
                      <span className="text-muted-foreground">Quantidade: </span>
                      <span>
                        {[suggestion.quantity, suggestion.unit]
                          .filter(Boolean)
                          .join(" ")}
                      </span>
                    </div>
                  )}
                  <div className="text-xs text-muted-foreground pt-1">
                    Fonte: {suggestion.sourceProvider}
                    {suggestion.confidence != null &&
                      ` • confiança ${Math.round(suggestion.confidence * 100)}%`}
                  </div>
                </div>
              </div>

              <div className="flex gap-2 pt-1">
                <Button
                  type="button"
                  size="sm"
                  onClick={handleApplySuggestion}
                >
                  Aplicar sugestão
                </Button>
                <Button
                  type="button"
                  variant="outline"
                  size="sm"
                  onClick={handleDismiss}
                >
                  Ignorar
                </Button>
              </div>
            </CardContent>
          </Card>
        )}

        {/* Not found message */}
        {lookupStatus === "not_found" && (
          <p className="text-sm text-muted-foreground">
            Produto não encontrado nessa base. Cadastre manualmente.
          </p>
        )}

        {/* Unavailable message */}
        {lookupStatus === "unavailable" && (
          <p className="text-sm text-muted-foreground">
            Consulta indisponível no momento. Cadastre manualmente e tente novamente depois.
          </p>
        )}
      </div>

      {/* Main data grid */}
      <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-3 gap-5">
        <div className="space-y-1.5">
          <Label>Código interno</Label>
          <Input
            value={data.code ?? ""}
            onChange={(e) => onChange("code", e.target.value)}
            placeholder="PRD-000"
          />
        </div>
        <div className="space-y-1.5 md:col-span-2 lg:col-span-2">
          <Label>Nome do produto</Label>
          <Input
            value={data.name ?? ""}
            onChange={(e) => onChange("name", e.target.value)}
            placeholder="Nome do produto"
          />
        </div>
        <div className="space-y-1.5 md:col-span-2 lg:col-span-3">
          <Label>Descrição adicional</Label>
          <Input
            value={data.description ?? ""}
            onChange={(e) => onChange("description", e.target.value)}
            placeholder="Informações complementares sobre o produto"
          />
        </div>
        <div className="space-y-1.5">
          <Label>Categoria</Label>
          <Select
            value={data.categoryId ?? "none"}
            onValueChange={(v) => onChange("categoryId", v === "none" ? null : v)}
          >
            <SelectTrigger>
              <SelectValue placeholder="Selecione" />
            </SelectTrigger>
            <SelectContent>
              <SelectItem value="none">Sem categoria</SelectItem>
              {categories.map((c) => (
                <SelectItem key={c.id} value={c.id}>{c.name}</SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
        <div className="space-y-1.5">
          <Label>Unidade de medida</Label>
          <Select
            value={data.unit ?? "Un"}
            onValueChange={(v) => onChange("unit", v)}
          >
            <SelectTrigger><SelectValue /></SelectTrigger>
            <SelectContent>
              {Object.entries(productUnitLabels).map(([value, label]) => (
                <SelectItem key={value} value={value}>{label}</SelectItem>
              ))}
            </SelectContent>
          </Select>
        </div>
        <div className="space-y-1.5">
          <Label>Controle de estoque</Label>
          <div className="flex items-center gap-3 pt-2">
            <Switch
              checked={data.trackStock ?? true}
              onCheckedChange={(v) => onChange("trackStock", v)}
            />
            <span className="text-sm text-muted-foreground">
              {data.trackStock ? "Ativo" : "Inativo"}
            </span>
          </div>
        </div>
        <div className="flex items-center gap-3 pt-5">
          <Switch
            checked={data.isActive ?? true}
            onCheckedChange={(v) => onChange("isActive", v)}
          />
          <Label>Produto ativo</Label>
        </div>
        {data.id && (
          <div className="space-y-1.5 col-span-full">
            <Label>Imagem do produto</Label>
            <ImageUploadButton
              context="product-image"
              value={data.imageUrl ?? null}
              onChange={handleImageChange}
              label="Imagem"
            />
          </div>
        )}
      </div>
    </div>
  );
}
