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
import {
  FISCAL_KEY, TAXPAYER_INDICATORS, getCustomerFiscal, saveCustomerFiscal,
  type CustomerFiscalInput, type TaxpayerIndicator,
} from "../api/fiscal.api";
import { customerFiscalProblems } from "../lib/validation";

const EMPTY: CustomerFiscalInput = { stateRegistration: null, taxpayerIndicator: null, cityIbgeCode: null };

/** Dados fiscais de um cliente empresa: inscrição estadual, indicador e município (IBGE) para a NF-e. */
export function CustomerFiscalDialog({ customerId, open, onOpenChange }: Readonly<{
  customerId: string; open: boolean; onOpenChange: (open: boolean) => void;
}>) {
  const qc = useQueryClient();
  const [form, setForm] = useState<CustomerFiscalInput>(EMPTY);
  const customer = useQuery({
    queryKey: [...FISCAL_KEY, "customer", customerId],
    queryFn: () => getCustomerFiscal(customerId),
    enabled: open,
    refetchOnWindowFocus: false,
  });

  useEffect(() => {
    if (!open || !customer.data) {
      setForm(EMPTY);
      return;
    }
    const { stateRegistration, taxpayerIndicator, cityIbgeCode } = customer.data;
    setForm({ stateRegistration, taxpayerIndicator, cityIbgeCode });
  }, [open, customer.data]);

  const save = useMutation({
    mutationFn: () => saveCustomerFiscal(customerId, form),
    onSuccess: () => {
      toast.success("Dados fiscais do cliente salvos.");
      void qc.invalidateQueries({ queryKey: [...FISCAL_KEY, "customer", customerId] });
      onOpenChange(false);
    },
    onError: (e: Error) => toast.error(e.message),
  });

  const problems = customerFiscalProblems(form);

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent className="max-w-lg">
        <DialogHeader>
          <DialogTitle>Dados fiscais do cliente</DialogTitle>
          <DialogDescription>Usados na NF-e de uma venda para empresa. Para consumidor final basta o CPF.</DialogDescription>
        </DialogHeader>
        {customer.isError && <p className="text-sm text-destructive">Não foi possível carregar os dados fiscais deste cliente.</p>}
        {customer.isLoading && <Skeleton className="h-40 w-full" />}
        {customer.data && (
          <form id="customer-fiscal-form" className="grid gap-3"
            onSubmit={(e) => { e.preventDefault(); if (problems.length === 0) save.mutate(); }}>
            <p className="text-sm text-muted-foreground">
              {customer.data.documentType}: {customer.data.documentNumber || "não informado"}
              {customer.data.documentNumber && !customer.data.documentValid && (
                <span className="text-warning"> — número inválido; corrija no cadastro antes de emitir nota.</span>
              )}
            </p>
            <div className="space-y-1">
              <Label htmlFor="cf-indicator">Indicador da inscrição estadual</Label>
              <Select
                value={form.taxpayerIndicator ?? "none"}
                onValueChange={(v) => setForm({ ...form, taxpayerIndicator: v === "none" ? null : (v as TaxpayerIndicator) })}
              >
                <SelectTrigger id="cf-indicator"><SelectValue /></SelectTrigger>
                <SelectContent>
                  <SelectItem value="none">Não informado</SelectItem>
                  {TAXPAYER_INDICATORS.map((t) => <SelectItem key={t.value} value={t.value}>{t.label}</SelectItem>)}
                </SelectContent>
              </Select>
            </div>
            <div className="space-y-1">
              <Label htmlFor="cf-ie">Inscrição estadual</Label>
              <Input id="cf-ie" placeholder="Números ou ISENTO" value={form.stateRegistration ?? ""}
                onChange={(e) => setForm({ ...form, stateRegistration: e.target.value || null })} />
            </div>
            <div className="space-y-1">
              <Label htmlFor="cf-ibge">Código IBGE do município</Label>
              <Input id="cf-ibge" inputMode="numeric" placeholder="3550308" value={form.cityIbgeCode ?? ""}
                onChange={(e) => setForm({ ...form, cityIbgeCode: e.target.value || null })} />
            </div>
            {problems.length > 0 && (
              <ul className="list-disc pl-5 text-sm text-destructive">{problems.map((p) => <li key={p}>{p}</li>)}</ul>
            )}
          </form>
        )}
        <DialogFooter>
          <Button variant="outline" onClick={() => onOpenChange(false)}>Cancelar</Button>
          <Button type="submit" form="customer-fiscal-form" disabled={problems.length > 0 || save.isPending || !customer.data}>
            {save.isPending ? "Salvando…" : "Salvar"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
