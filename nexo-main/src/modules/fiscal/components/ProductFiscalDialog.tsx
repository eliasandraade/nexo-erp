import { useEffect, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Skeleton } from "@/components/ui/skeleton";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import {
  Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle,
} from "@/components/ui/dialog";
import { isGtin } from "@/lib/fiscal-codes";
import { FISCAL_KEY, ORIGINS, getProductFiscal, saveProductFiscal, type ProductFiscalInput } from "../api/fiscal.api";
import { fiscalProblems } from "../lib/validation";

const EMPTY: ProductFiscalInput = { ncm: null, cest: null, origin: null, cfop: null, taxSituation: null, taxUnit: null };

/**
 * Dados fiscais de um produto — o que o contador informa (NCM, CEST, origem, CFOP, CST/CSOSN).
 * Nada aqui calcula imposto.
 */
export function ProductFiscalDialog({ productId, open, onOpenChange }: Readonly<{
  productId: string | null; open: boolean; onOpenChange: (open: boolean) => void;
}>) {
  const qc = useQueryClient();
  const [form, setForm] = useState<ProductFiscalInput>(EMPTY);

  const product = useQuery({
    queryKey: [...FISCAL_KEY, "product", productId],
    queryFn: () => getProductFiscal(productId!),
    enabled: open && !!productId,
    refetchOnWindowFocus: false,   // a refetch would reset what is being typed
  });

  // Every opening starts from what is saved — never from edits that were cancelled, nor from the
  // previous product's values while the next one loads.
  useEffect(() => {
    if (!open || !product.data || product.data.productId !== productId) {
      setForm(EMPTY);
      return;
    }
    const { ncm, cest, origin, cfop, taxSituation, taxUnit } = product.data;
    setForm({ ncm, cest, origin, cfop, taxSituation, taxUnit });
  }, [open, productId, product.data]);

  const save = useMutation({
    mutationFn: () => saveProductFiscal(productId!, form),
    onSuccess: () => {
      toast.success("Dados fiscais salvos.");
      void qc.invalidateQueries({ queryKey: FISCAL_KEY });
      onOpenChange(false);
    },
    onError: (e: Error) => toast.error(e.message),
  });

  const problems = fiscalProblems(form);
  const set = (key: keyof ProductFiscalInput) => (e: React.ChangeEvent<HTMLInputElement>) =>
    setForm({ ...form, [key]: e.target.value || null });
  const barcode = product.data?.barcode;

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-lg">
        <DialogHeader>
          <DialogTitle>Dados fiscais{product.data ? ` — ${product.data.name}` : ""}</DialogTitle>
          <DialogDescription>Peça ao seu contador. O Orken guarda estes dados para a nota; não calcula imposto.</DialogDescription>
        </DialogHeader>
        {product.isError && <p className="text-sm text-destructive">Não foi possível carregar os dados fiscais deste produto.</p>}
        {product.isLoading && <Skeleton className="h-48 w-full" />}
        {product.data && (
          <form
            id="product-fiscal-form"
            className="grid gap-3 sm:grid-cols-2"
            onSubmit={(e) => { e.preventDefault(); if (problems.length === 0) save.mutate(); }}
          >
            <div className="space-y-1">
              <Label htmlFor="pf-ncm">NCM</Label>
              <Input id="pf-ncm" inputMode="numeric" placeholder="0000.00.00" value={form.ncm ?? ""} onChange={set("ncm")} />
            </div>
            <div className="space-y-1">
              <Label htmlFor="pf-cest">CEST (se houver ST)</Label>
              <Input id="pf-cest" inputMode="numeric" placeholder="00.000.00" value={form.cest ?? ""} onChange={set("cest")} />
            </div>
            <div className="space-y-1 sm:col-span-2">
              <Label htmlFor="pf-origin">Origem da mercadoria</Label>
              <Select
                value={form.origin === null ? "none" : String(form.origin)}
                onValueChange={(v) => setForm({ ...form, origin: v === "none" ? null : Number(v) })}
              >
                <SelectTrigger id="pf-origin"><SelectValue /></SelectTrigger>
                <SelectContent>
                  <SelectItem value="none">Não informada</SelectItem>
                  {ORIGINS.map((o) => <SelectItem key={o.value} value={String(o.value)}>{o.label}</SelectItem>)}
                </SelectContent>
              </Select>
            </div>
            <div className="space-y-1">
              <Label htmlFor="pf-cfop">CFOP de venda</Label>
              <Input id="pf-cfop" inputMode="numeric" placeholder="5102" value={form.cfop ?? ""} onChange={set("cfop")} />
            </div>
            <div className="space-y-1">
              <Label htmlFor="pf-cst">CST / CSOSN do ICMS</Label>
              <Input id="pf-cst" inputMode="numeric" placeholder="102" value={form.taxSituation ?? ""} onChange={set("taxSituation")} />
            </div>
            <div className="space-y-1">
              <Label htmlFor="pf-unit">Unidade tributável</Label>
              <Input id="pf-unit" placeholder={product.data.unit ?? "UN"} value={form.taxUnit ?? ""} onChange={set("taxUnit")} />
            </div>
            {barcode && !isGtin(barcode) && (
              <p className="text-sm text-warning sm:col-span-2">
                O código de barras ({barcode}) não é um GTIN válido: na nota ele vai como “SEM GTIN”. Corrija no cadastro do produto se for um código de fábrica.
              </p>
            )}
            {problems.length > 0 && (
              <ul className="list-disc pl-5 text-sm text-destructive sm:col-span-2">
                {problems.map((p) => <li key={p}>{p}</li>)}
              </ul>
            )}
          </form>
        )}
        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)}>Cancelar</Button>
          <Button type="submit" form="product-fiscal-form" disabled={problems.length > 0 || save.isPending || !product.data || product.isError}>
            {save.isPending ? "Salvando…" : "Salvar"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
