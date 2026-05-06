import { useState, useEffect, useRef } from "react";
import { useParams, useNavigate } from "react-router-dom";
import { ArrowLeft, Trash2, ImagePlus, ImageOff, Loader2 } from "lucide-react";
import { toast } from "sonner";

import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { Switch } from "@/components/ui/switch";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import {
  Card,
  CardContent,
  CardHeader,
  CardTitle,
} from "@/components/ui/card";

import { PageHeader } from "@/components/shared/PageHeader";

import { PrepStepsEditor } from "../components/PrepStepsEditor";
import { CmvBar } from "../components/CmvBar";

import {
  useRecipeCardByProduct,
  useCreateRecipeCard,
  useUpdateRecipeCard,
  useAddIngredient,
  useRemoveIngredient,
  useUploadRecipeImage,
} from "../hooks/use-recipe-card";

import { useProducts } from "@/modules/products/hooks/use-products";

import type { PrepStepDto, RecipeCardDto } from "../types/recipe-card.types";

// ── Helpers ───────────────────────────────────────────────────────────────────

function fmt(v: number) {
  return v.toLocaleString("pt-BR", { style: "currency", currency: "BRL" });
}

function is404(err: unknown): boolean {
  if (!err) return false;
  const e = err as { status?: number; response?: { status?: number } };
  return e.status === 404 || e.response?.status === 404;
}

// ── Create form (shown when no card exists) ───────────────────────────────────

interface CreateFormProps {
  productId: string;
}

function CreateForm({ productId }: CreateFormProps) {
  const [yieldVal, setYieldVal] = useState(1);
  const [yieldUnit, setYieldUnit] = useState("un");
  const createMut = useCreateRecipeCard(productId);

  const handleCreate = () => {
    if (yieldVal <= 0) {
      toast.error("Rendimento deve ser maior que zero.");
      return;
    }
    createMut.mutate(
      { productId, yield: yieldVal, yieldUnit: yieldUnit.trim() || "un" },
      {
        onError: () => toast.error("Erro ao criar ficha técnica."),
      }
    );
  };

  return (
    <Card className="max-w-md">
      <CardHeader>
        <CardTitle className="text-base">Criar Ficha Técnica</CardTitle>
      </CardHeader>
      <CardContent className="space-y-4">
        <p className="text-sm text-muted-foreground">
          Este produto ainda não possui ficha técnica. Informe o rendimento inicial para criá-la.
        </p>
        <div className="flex gap-3">
          <div className="flex-1 space-y-1.5">
            <Label htmlFor="yield-init">Rendimento</Label>
            <Input
              id="yield-init"
              type="number"
              min={0.001}
              step={0.001}
              value={yieldVal}
              onChange={(e) => setYieldVal(parseFloat(e.target.value) || 1)}
            />
          </div>
          <div className="flex-1 space-y-1.5">
            <Label htmlFor="yield-unit-init">Unidade</Label>
            <Input
              id="yield-unit-init"
              value={yieldUnit}
              onChange={(e) => setYieldUnit(e.target.value)}
              placeholder="un, kg, porcao…"
            />
          </div>
        </div>
        <Button
          className="w-full"
          onClick={handleCreate}
          disabled={createMut.isPending}
        >
          {createMut.isPending && <Loader2 className="h-4 w-4 mr-2 animate-spin" />}
          Criar Ficha Técnica
        </Button>
      </CardContent>
    </Card>
  );
}

// ── Full editor ───────────────────────────────────────────────────────────────

interface EditorProps {
  card: RecipeCardDto;
  productId: string;
}

function RecipeCardEditor({ card, productId }: EditorProps) {
  const fileInputRef = useRef<HTMLInputElement>(null);

  // ── Mutations
  const updateMut      = useUpdateRecipeCard(card.id, productId);
  const addIngMut      = useAddIngredient(card.id, productId);
  const removeIngMut   = useRemoveIngredient(card.id, productId);
  const uploadImageMut = useUploadRecipeImage(card.id, productId);

  // ── Products for ingredient / packaging selects
  const { data: ingredients = [] } = useProducts({ isIngredient: true });

  // ── Local form state — mirrors card fields
  const [yieldVal, setYieldVal]             = useState(card.yield);
  const [yieldUnit, setYieldUnit]           = useState(card.yieldUnit);
  const [hasPrep, setHasPrep]               = useState(card.hasPrep);
  const [prepSteps, setPrepSteps]           = useState<PrepStepDto[]>(card.prepSteps);
  const [assemblyNotes, setAssemblyNotes]   = useState(card.assemblyNotes ?? "");
  const [requiresPackaging, setRequiresPkg] = useState(card.requiresPackaging);
  const [packagingProductId, setPkgId]      = useState<string>(card.packagingProductId ?? "");
  const [notes, setNotes]                   = useState(card.notes ?? "");

  // ── Add-ingredient form state
  const [selIngId, setSelIngId] = useState<string>("");
  const [qty, setQty]           = useState<number>(1);
  const [unit, setUnit]         = useState<string>("");

  // Reset local state when card changes (e.g. after save)
  useEffect(() => {
    setYieldVal(card.yield);
    setYieldUnit(card.yieldUnit);
    setHasPrep(card.hasPrep);
    setPrepSteps(card.prepSteps);
    setAssemblyNotes(card.assemblyNotes ?? "");
    setRequiresPkg(card.requiresPackaging);
    setPkgId(card.packagingProductId ?? "");
    setNotes(card.notes ?? "");
  }, [card]);

  // Pre-fill unit when ingredient is selected
  const handleSelectIngredient = (id: string) => {
    setSelIngId(id);
    const found = ingredients.find((p) => p.id === id);
    if (found) setUnit(found.unit);
  };

  const handleAddIngredient = () => {
    if (!selIngId) { toast.error("Selecione um ingrediente."); return; }
    if (qty <= 0)  { toast.error("Quantidade deve ser maior que zero."); return; }
    addIngMut.mutate(
      { ingredientProductId: selIngId, quantity: qty, unit: unit || "un" },
      {
        onSuccess: () => { setSelIngId(""); setQty(1); setUnit(""); },
        onError:   () => toast.error("Erro ao adicionar ingrediente."),
      }
    );
  };

  const handleSave = () => {
    updateMut.mutate(
      {
        yield:              yieldVal,
        yieldUnit:          yieldUnit.trim() || "un",
        hasPrep,
        prepSteps,
        assemblyNotes:      assemblyNotes.trim() || null,
        requiresPackaging,
        packagingProductId: requiresPackaging && packagingProductId ? packagingProductId : null,
        notes:              notes.trim() || null,
      },
      {
        onSuccess: () => toast.success("Ficha técnica salva!"),
        onError:   () => toast.error("Erro ao salvar ficha técnica."),
      }
    );
  };

  const handleFileChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (!file) return;
    uploadImageMut.mutate(file, {
      onSuccess: () => toast.success("Imagem atualizada!"),
      onError:   () => toast.error("Erro ao enviar imagem."),
    });
    // reset input so the same file can be re-selected
    e.target.value = "";
  };

  return (
    <div className="flex flex-col gap-0">
      {/* Two-column grid */}
      <div className="grid grid-cols-1 md:grid-cols-2 gap-6 p-6 pb-20">

        {/* ── LEFT COLUMN ───────────────────────────────────────────── */}
        <div className="space-y-5">

          {/* Photo */}
          <Card>
            <CardHeader className="pb-3">
              <CardTitle className="text-sm">Foto do produto</CardTitle>
            </CardHeader>
            <CardContent className="space-y-3">
              {card.imageUrl ? (
                <img
                  src={card.imageUrl}
                  alt={card.productName}
                  className="w-full h-48 object-cover rounded-md border border-border"
                />
              ) : (
                <div className="w-full h-48 flex items-center justify-center rounded-md border border-dashed border-border bg-muted/30">
                  <ImageOff className="h-8 w-8 text-muted-foreground" />
                </div>
              )}
              <input
                ref={fileInputRef}
                type="file"
                accept="image/jpeg,image/png,image/webp"
                className="hidden"
                onChange={handleFileChange}
              />
              <Button
                variant="outline"
                size="sm"
                className="w-full"
                disabled={uploadImageMut.isPending}
                onClick={() => fileInputRef.current?.click()}
              >
                {uploadImageMut.isPending ? (
                  <Loader2 className="h-4 w-4 mr-2 animate-spin" />
                ) : (
                  <ImagePlus className="h-4 w-4 mr-2" />
                )}
                {card.imageUrl ? "Alterar foto" : "Adicionar foto"}
              </Button>
            </CardContent>
          </Card>

          {/* Rendimento */}
          <Card>
            <CardHeader className="pb-3">
              <CardTitle className="text-sm">Rendimento</CardTitle>
            </CardHeader>
            <CardContent className="flex gap-3">
              <div className="flex-1 space-y-1.5">
                <Label htmlFor="yield">Rendimento</Label>
                <Input
                  id="yield"
                  type="number"
                  min={0.001}
                  step={0.001}
                  value={yieldVal}
                  onChange={(e) => setYieldVal(parseFloat(e.target.value) || 1)}
                />
              </div>
              <div className="flex-1 space-y-1.5">
                <Label htmlFor="yield-unit">Unidade de rendimento</Label>
                <Input
                  id="yield-unit"
                  value={yieldUnit}
                  onChange={(e) => setYieldUnit(e.target.value)}
                  placeholder="un, kg, porcao…"
                />
              </div>
            </CardContent>
          </Card>

          {/* Modo de preparo toggle */}
          <Card>
            <CardContent className="pt-5">
              <div className="flex items-center justify-between">
                <div>
                  <p className="text-sm font-medium">Tem preparo?</p>
                  <p className="text-xs text-muted-foreground mt-0.5">
                    Inclui etapas de preparo na ficha.
                  </p>
                </div>
                <Switch
                  checked={hasPrep}
                  onCheckedChange={setHasPrep}
                />
              </div>

              {hasPrep && (
                <div className="mt-4 pt-4 border-t border-border">
                  <p className="text-xs font-medium text-muted-foreground mb-3 uppercase tracking-wide">
                    Passos de preparo
                  </p>
                  <PrepStepsEditor steps={prepSteps} onChange={setPrepSteps} />
                </div>
              )}
            </CardContent>
          </Card>

          {/* Ingredientes */}
          <Card>
            <CardHeader className="pb-3">
              <CardTitle className="text-sm">Ingredientes</CardTitle>
            </CardHeader>
            <CardContent className="space-y-3">
              {/* Current list */}
              {card.ingredients.length === 0 ? (
                <p className="text-xs text-muted-foreground py-2">
                  Nenhum ingrediente adicionado.
                </p>
              ) : (
                <div className="divide-y divide-border rounded-md border border-border overflow-hidden">
                  {card.ingredients.map((ing) => (
                    <div
                      key={ing.id}
                      className="flex items-center gap-2 px-3 py-2 text-sm bg-card"
                    >
                      <div className="flex-1 min-w-0">
                        <p className="font-medium truncate">{ing.ingredientName}</p>
                        <p className="text-xs text-muted-foreground">
                          {ing.quantity} {ing.unit} · {fmt(ing.lineCost)}
                        </p>
                      </div>
                      <Button
                        size="icon"
                        variant="ghost"
                        className="h-7 w-7 text-destructive shrink-0"
                        disabled={removeIngMut.isPending}
                        onClick={() =>
                          removeIngMut.mutate(ing.id, {
                            onError: () => toast.error("Erro ao remover ingrediente."),
                          })
                        }
                      >
                        <Trash2 className="h-3.5 w-3.5" />
                      </Button>
                    </div>
                  ))}
                </div>
              )}

              {/* Add ingredient form */}
              <div className="space-y-2 pt-1">
                <p className="text-xs font-medium text-muted-foreground uppercase tracking-wide">
                  Adicionar ingrediente
                </p>
                <Select value={selIngId} onValueChange={handleSelectIngredient}>
                  <SelectTrigger className="text-sm">
                    <SelectValue placeholder="Selecionar ingrediente…" />
                  </SelectTrigger>
                  <SelectContent>
                    {ingredients.map((p) => (
                      <SelectItem key={p.id} value={p.id}>
                        {p.name} ({p.code})
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
                <div className="flex gap-2">
                  <div className="flex-1 space-y-1">
                    <Label className="text-xs">Quantidade</Label>
                    <Input
                      type="number"
                      min={0.001}
                      step={0.001}
                      value={qty}
                      onChange={(e) => setQty(parseFloat(e.target.value) || 0)}
                      className="text-sm"
                      placeholder="0.001"
                    />
                  </div>
                  <div className="flex-1 space-y-1">
                    <Label className="text-xs">Unidade</Label>
                    <Input
                      value={unit}
                      onChange={(e) => setUnit(e.target.value)}
                      className="text-sm"
                      placeholder="un, kg, L…"
                    />
                  </div>
                </div>
                <Button
                  size="sm"
                  variant="outline"
                  className="w-full"
                  disabled={addIngMut.isPending || !selIngId}
                  onClick={handleAddIngredient}
                >
                  {addIngMut.isPending && <Loader2 className="h-3.5 w-3.5 mr-2 animate-spin" />}
                  Adicionar
                </Button>
              </div>
            </CardContent>
          </Card>
        </div>

        {/* ── RIGHT COLUMN ──────────────────────────────────────────── */}
        <div className="space-y-5">

          {/* Montagem */}
          <Card>
            <CardHeader className="pb-3">
              <CardTitle className="text-sm">Montagem</CardTitle>
            </CardHeader>
            <CardContent>
              <Textarea
                value={assemblyNotes}
                onChange={(e) => setAssemblyNotes(e.target.value)}
                placeholder="Instruções de montagem e apresentação..."
                className="min-h-[120px] text-sm resize-none"
              />
            </CardContent>
          </Card>

          {/* Embalagem */}
          <Card>
            <CardHeader className="pb-3">
              <CardTitle className="text-sm">Embalagem</CardTitle>
            </CardHeader>
            <CardContent className="space-y-4">
              <div className="flex items-center justify-between">
                <div>
                  <p className="text-sm font-medium">Requer embalagem?</p>
                  <p className="text-xs text-muted-foreground mt-0.5">
                    Associa uma embalagem ao custo do prato.
                  </p>
                </div>
                <Switch
                  checked={requiresPackaging}
                  onCheckedChange={setRequiresPkg}
                />
              </div>
              {requiresPackaging && (
                <div className="space-y-1.5">
                  <Label>Embalagem</Label>
                  <Select
                    value={packagingProductId}
                    onValueChange={setPkgId}
                  >
                    <SelectTrigger className="text-sm">
                      <SelectValue placeholder="Selecionar embalagem…" />
                    </SelectTrigger>
                    <SelectContent>
                      {ingredients.map((p) => (
                        <SelectItem key={p.id} value={p.id}>
                          {p.name} ({p.code})
                        </SelectItem>
                      ))}
                    </SelectContent>
                  </Select>
                </div>
              )}
            </CardContent>
          </Card>

          {/* Observações */}
          <Card>
            <CardHeader className="pb-3">
              <CardTitle className="text-sm">Observações</CardTitle>
            </CardHeader>
            <CardContent>
              <Textarea
                value={notes}
                onChange={(e) => setNotes(e.target.value)}
                placeholder="Observações internas sobre a ficha técnica…"
                className="min-h-[100px] text-sm resize-none"
              />
            </CardContent>
          </Card>

          {/* Custo summary (read-only) */}
          <Card className="bg-muted/30">
            <CardHeader className="pb-3">
              <CardTitle className="text-sm">Resumo de custo</CardTitle>
            </CardHeader>
            <CardContent className="space-y-2 text-sm">
              <div className="flex justify-between">
                <span className="text-muted-foreground">Ingredientes</span>
                <span className="font-medium">{fmt(card.ingredientCost)}</span>
              </div>
              {card.gasCost > 0 && (
                <div className="flex justify-between">
                  <span className="text-muted-foreground">Gás</span>
                  <span className="font-medium">{fmt(card.gasCost)}</span>
                </div>
              )}
              {card.laborCost > 0 && (
                <div className="flex justify-between">
                  <span className="text-muted-foreground">Mão de obra</span>
                  <span className="font-medium">{fmt(card.laborCost)}</span>
                </div>
              )}
              <div className="flex justify-between border-t border-border pt-2 mt-1">
                <span className="text-muted-foreground">Custo total</span>
                <span className="font-semibold">{fmt(card.calculatedCost)}</span>
              </div>
              <div className="flex justify-between">
                <span className="text-muted-foreground">Preço de venda</span>
                <span className="font-medium">{fmt(card.salePrice)}</span>
              </div>
            </CardContent>
          </Card>

          {/* Save */}
          <Button
            className="w-full"
            onClick={handleSave}
            disabled={updateMut.isPending}
          >
            {updateMut.isPending && <Loader2 className="h-4 w-4 mr-2 animate-spin" />}
            Salvar ficha
          </Button>
        </div>
      </div>

      {/* Sticky CMV bar */}
      <CmvBar
        ingredientCost={card.ingredientCost}
        gasCost={card.gasCost}
        laborCost={card.laborCost}
        calculatedCost={card.calculatedCost}
        salePrice={card.salePrice}
        cmvPercent={card.cmvPercent}
      />
    </div>
  );
}

// ── Main page ─────────────────────────────────────────────────────────────────

export default function RecipeCardPage() {
  const { id: productId = "" } = useParams<{ id: string }>();
  const navigate = useNavigate();

  const { data: card, isLoading, error } = useRecipeCardByProduct(productId);

  const noCard = !card && (is404(error) || (!isLoading && error));

  return (
    <div className="flex flex-col min-h-full">
      {/* Header */}
      <div className="px-6 pt-6 pb-4">
        <PageHeader
          title={card ? `Ficha Técnica — ${card.productName}` : "Ficha Técnica"}
          description={card ? `${card.productCode} · Rendimento: ${card.yield} ${card.yieldUnit}` : undefined}
          actions={
            <Button
              variant="outline"
              size="sm"
              onClick={() => navigate(`/produtos/${productId}`)}
            >
              <ArrowLeft className="h-4 w-4 mr-1.5" />
              Voltar ao produto
            </Button>
          }
        />
      </div>

      {/* Body */}
      {isLoading ? (
        <div className="flex flex-1 items-center justify-center py-20">
          <Loader2 className="h-8 w-8 animate-spin text-muted-foreground" />
        </div>
      ) : noCard ? (
        <div className="px-6 py-4">
          <CreateForm productId={productId} />
        </div>
      ) : error ? (
        <div className="px-6 py-10 text-center text-sm text-destructive">
          Erro ao carregar ficha técnica. Tente novamente.
        </div>
      ) : card ? (
        <RecipeCardEditor card={card} productId={productId} />
      ) : null}
    </div>
  );
}
