import { useState } from "react";
import { useNavigate } from "react-router-dom";
import { Loader2, Package, ArrowRight, CheckCircle } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { useAuth } from "@/modules/auth/context/AuthContext";
import { apiClient } from "@/services/api-client";

interface Props {
  onComplete: () => void;
}

export function OnboardingWizard({ onComplete }: Props) {
  const { session } = useAuth();
  const navigate = useNavigate();
  const [step, setStep] = useState(0);
  const [productName, setProductName] = useState("");
  const [price, setPrice] = useState("");
  const [stock, setStock] = useState("");
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  async function createProduct() {
    setError(null);
    if (!productName.trim()) return setError("Informe o nome do produto.");
    const priceNum = parseFloat(price.replace(",", "."));
    if (!price || isNaN(priceNum) || priceNum < 0) return setError("Informe um preço válido.");

    setLoading(true);
    try {
      const product = await apiClient.post<{ id: string }>("/products", {
        name:      productName.trim(),
        code:      "",
        price:     priceNum,
        costPrice: 0,
      });

      const stockNum = parseInt(stock) || 0;
      if (stockNum > 0) {
        await apiClient.post("/stock/adjust", {
          productId: product.id,
          quantity:  stockNum,
          type:      "EntradaManual",
          reason:    "Estoque inicial",
        });
      }

      setStep(2);
    } catch {
      setError("Erro ao criar produto. Tente novamente.");
    } finally {
      setLoading(false);
    }
  }

  function finish() {
    if (session) localStorage.removeItem(`nexo:onboarding:${session.userId}`);
    onComplete();
    navigate("/pdv");
  }

  return (
    <div className="fixed inset-0 bg-background/80 backdrop-blur-sm z-50 flex items-center justify-center p-4">
      <div className="bg-card border border-border rounded-xl shadow-lg w-full max-w-md p-6 space-y-6">
        {/* Progress */}
        <div className="flex gap-1.5">
          {[0, 1, 2].map((i) => (
            <div
              key={i}
              className={`h-1 flex-1 rounded-full transition-colors ${
                i <= step ? "bg-primary" : "bg-muted"
              }`}
            />
          ))}
        </div>

        {/* Step 0: Welcome */}
        {step === 0 && (
          <div className="space-y-4 text-center">
            <div className="w-14 h-14 bg-primary/10 rounded-full flex items-center justify-center mx-auto">
              <Package className="h-7 w-7 text-primary" />
            </div>
            <div className="space-y-1">
              <h2 className="text-lg font-semibold text-foreground">
                Bem-vindo ao Orken{session?.companyName ? `, ${session.companyName}` : ""}!
              </h2>
              <p className="text-sm text-muted-foreground">
                Vamos configurar seu primeiro produto para você poder fazer sua primeira venda.
              </p>
            </div>
            <Button className="w-full" onClick={() => setStep(1)}>
              Começar <ArrowRight className="h-4 w-4 ml-1" />
            </Button>
          </div>
        )}

        {/* Step 1: Create product */}
        {step === 1 && (
          <div className="space-y-4">
            <div>
              <h2 className="text-lg font-semibold text-foreground">Crie seu primeiro produto</h2>
              <p className="text-sm text-muted-foreground mt-0.5">
                Pode editar depois — só preencha o básico agora.
              </p>
            </div>
            <div className="space-y-3">
              <div className="space-y-1.5">
                <Label>Nome do produto</Label>
                <Input
                  placeholder="Ex: Camiseta Branca M"
                  value={productName}
                  onChange={(e) => setProductName(e.target.value)}
                  autoFocus
                />
              </div>
              <div className="space-y-1.5">
                <Label>Preço de venda (R$)</Label>
                <Input
                  placeholder="0,00"
                  value={price}
                  onChange={(e) => setPrice(e.target.value)}
                />
              </div>
              <div className="space-y-1.5">
                <Label>
                  Estoque inicial{" "}
                  <span className="text-muted-foreground text-xs">(opcional)</span>
                </Label>
                <Input
                  type="number"
                  min="0"
                  placeholder="0"
                  value={stock}
                  onChange={(e) => setStock(e.target.value)}
                />
              </div>
            </div>
            {error && <p className="text-sm text-destructive">{error}</p>}
            <Button className="w-full" onClick={createProduct} disabled={loading}>
              {loading && <Loader2 className="h-4 w-4 mr-2 animate-spin" />}
              Criar produto
            </Button>
          </div>
        )}

        {/* Step 2: Done */}
        {step === 2 && (
          <div className="space-y-4 text-center">
            <CheckCircle className="h-12 w-12 text-green-500 mx-auto" />
            <div className="space-y-1">
              <h2 className="text-lg font-semibold text-foreground">Tudo pronto!</h2>
              <p className="text-sm text-muted-foreground">
                Produto criado. Agora é só fazer sua primeira venda pelo PDV.
              </p>
            </div>
            <Button className="w-full" onClick={finish}>
              Ir para o PDV <ArrowRight className="h-4 w-4 ml-1" />
            </Button>
          </div>
        )}
      </div>
    </div>
  );
}
