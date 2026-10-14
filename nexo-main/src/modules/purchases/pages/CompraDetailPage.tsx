import { useState } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { ArrowLeft, PackageCheck, Send, Trash2, X } from "lucide-react";
import { toast } from "sonner";
import { PageHeader } from "@/components/shared/PageHeader";
import { SectionCard } from "@/components/shared/SectionCard";
import { ErrorState } from "@/components/shared/ErrorState";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { Checkbox } from "@/components/ui/checkbox";
import { Dialog, DialogContent, DialogDescription, DialogFooter, DialogHeader, DialogTitle } from "@/components/ui/dialog";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Skeleton } from "@/components/ui/skeleton";
import { Textarea } from "@/components/ui/textarea";
import { Select, SelectContent, SelectItem, SelectTrigger, SelectValue } from "@/components/ui/select";
import { Table, TableBody, TableCell, TableHead, TableHeader, TableRow } from "@/components/ui/table";
import { formatCurrency } from "@/lib/formatters";
import { useAuth } from "@/modules/auth/context/AuthContext";
import { useProducts } from "@/modules/products/hooks/use-products";
import {
  addPurchaseItem, cancelPurchase, getPurchase, orderPurchase, receivePurchase, removePurchaseItem,
  PURCHASES_KEY, PURCHASE_STATUS_LABEL, type PurchaseDto,
} from "../api/purchases.api";


const num = (raw: string) => Number(raw.replace(",", "."));

export default function CompraDetailPage() {
  const { id } = useParams<{ id: string }>();
  const navigate = useNavigate();
  const qc = useQueryClient();
  const { session } = useAuth();
  const isManagement = session?.role === "gerente" || session?.role === "diretoria";
  const { data: purchase, isLoading, isError, refetch } = useQuery({
    queryKey: [...PURCHASES_KEY, id],
    queryFn: () => getPurchase(id!),
    enabled: !!id,
  });
  const [receiveOpen, setReceiveOpen] = useState(false);
  const [cancelOpen, setCancelOpen] = useState(false);

  const done = (p: PurchaseDto, message?: string) => {
    qc.setQueryData([...PURCHASES_KEY, id], p);
    qc.invalidateQueries({ queryKey: PURCHASES_KEY });
    qc.invalidateQueries({ queryKey: ["stock"] });
    if (message) toast.success(message);
  };
  const fail = (e: Error) => toast.error(e.message);

  const order = useMutation({ mutationFn: () => orderPurchase(id!), onSuccess: (p) => done(p, "Pedido enviado ao fornecedor."), onError: fail });
  const remove = useMutation({ mutationFn: (itemId: string) => removePurchaseItem(id!, itemId), onSuccess: (p) => done(p), onError: fail });

  if (isLoading) return <Skeleton className="h-64 w-full" />;
  if (isError || !purchase) return <ErrorState title="Compra não encontrada" onRetry={() => void refetch()} />;

  const editable = purchase.status === "Draft";
  const receivable = purchase.status === "Draft" || purchase.status === "Ordered";

  return (
    <div className="space-y-6">
      <PageHeader
        title={`Compra #${purchase.purchaseNumber}`}
        description={`${purchase.supplierName} · ${PURCHASE_STATUS_LABEL[purchase.status]}`}
        actions={
          <div className="flex flex-wrap items-center gap-2">
            {editable && (
              <Button variant="outline" onClick={() => order.mutate()} disabled={order.isPending || purchase.items.length === 0}>
                <Send className="mr-1.5 h-4 w-4" /> Enviar pedido
              </Button>
            )}
            {receivable && isManagement && (
              <Button onClick={() => setReceiveOpen(true)} disabled={purchase.items.length === 0}>
                <PackageCheck className="mr-1.5 h-4 w-4" /> Receber mercadoria
              </Button>
            )}
            {purchase.status !== "Cancelled" && isManagement && (
              <Button variant="destructive" onClick={() => setCancelOpen(true)}>
                <X className="mr-1.5 h-4 w-4" /> Cancelar
              </Button>
            )}
            <Button variant="outline" size="sm" onClick={() => navigate("/compras")}>
              <ArrowLeft className="mr-1.5 h-4 w-4" /> Voltar
            </Button>
          </div>
        }
      />

      {purchase.cancellationReason && (
        <SectionCard><p className="text-sm">Motivo do cancelamento: {purchase.cancellationReason}</p></SectionCard>
      )}

      <SectionCard
        title="Itens"
        description={editable ? "Edite os itens enquanto o pedido está em elaboração." : "Itens congelados."}
      >
        <div className="space-y-4">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Produto</TableHead>
                <TableHead className="text-right">Qtd.</TableHead>
                <TableHead className="text-right">Custo unit.</TableHead>
                <TableHead className="text-right">Total</TableHead>
                {editable && <TableHead className="w-10" />}
              </TableRow>
            </TableHeader>
            <TableBody>
              {purchase.items.map((i) => (
                <TableRow key={i.id}>
                  <TableCell className="text-sm">
                    {i.productName}<span className="block text-xs text-muted-foreground">{i.productCode}</span>
                  </TableCell>
                  <TableCell className="text-right tabular-nums">{i.quantity.toLocaleString("pt-BR")}</TableCell>
                  <TableCell className="text-right tabular-nums">{formatCurrency(i.unitCost)}</TableCell>
                  <TableCell className="text-right tabular-nums">{formatCurrency(i.total)}</TableCell>
                  {editable && (
                    <TableCell>
                      <Button variant="ghost" size="icon" aria-label={`Remover ${i.productName}`} onClick={() => remove.mutate(i.id)}>
                        <Trash2 className="h-4 w-4" />
                      </Button>
                    </TableCell>
                  )}
                </TableRow>
              ))}
            </TableBody>
          </Table>
          <div className="flex justify-end text-sm font-semibold">Total: {formatCurrency(purchase.totalAmount)}</div>
          {editable && <AddItemForm purchaseId={purchase.id} onAdded={(p) => done(p)} />}
        </div>
      </SectionCard>

      <ReceiveDialog open={receiveOpen} purchase={purchase} onClose={() => setReceiveOpen(false)}
        onDone={(p) => { setReceiveOpen(false); done(p, "Mercadoria recebida: estoque e contas a pagar atualizados."); }} />
      <CancelDialog open={cancelOpen} purchase={purchase} onClose={() => setCancelOpen(false)}
        onDone={(p) => { setCancelOpen(false); done(p, "Compra cancelada."); }} />
    </div>
  );
}

function AddItemForm({ purchaseId, onAdded }: { purchaseId: string; onAdded: (p: PurchaseDto) => void }) {
  const { data: products = [] } = useProducts();
  const [productId, setProductId] = useState("");
  const [quantity, setQuantity] = useState("");
  const [unitCost, setUnitCost] = useState("");
  const q = num(quantity), c = num(unitCost);
  const valid = !!productId && q > 0 && c >= 0 && unitCost.trim() !== "";

  const add = useMutation({
    mutationFn: () => addPurchaseItem(purchaseId, { productId, quantity: q, unitCost: c }),
    onSuccess: (p) => { setQuantity(""); setUnitCost(""); onAdded(p); },
    onError: (e: Error) => toast.error(e.message),
  });

  return (
    <div className="grid gap-3 rounded-md border p-3 sm:grid-cols-[1fr_7rem_8rem_auto] sm:items-end">
      <div className="space-y-1.5">
        <Label htmlFor="purchase-product">Produto</Label>
        <Select value={productId} onValueChange={(v) => {
          setProductId(v);
          const p = products.find((x) => x.id === v);
          if (p && unitCost.trim() === "") setUnitCost(String(p.costPrice));
        }}>
          <SelectTrigger id="purchase-product"><SelectValue placeholder="Escolha o produto" /></SelectTrigger>
          <SelectContent>
            {products.map((p) => <SelectItem key={p.id} value={p.id}>{p.name} ({p.code})</SelectItem>)}
          </SelectContent>
        </Select>
      </div>
      <div className="space-y-1.5">
        <Label htmlFor="purchase-qty">Quantidade</Label>
        <Input id="purchase-qty" inputMode="decimal" value={quantity} onChange={(e) => setQuantity(e.target.value)} />
      </div>
      <div className="space-y-1.5">
        <Label htmlFor="purchase-cost">Custo unitário</Label>
        <Input id="purchase-cost" inputMode="decimal" value={unitCost} onChange={(e) => setUnitCost(e.target.value)} />
      </div>
      <Button onClick={() => add.mutate()} disabled={!valid || add.isPending}>Adicionar</Button>
    </div>
  );
}

function ReceiveDialog({ open, purchase, onClose, onDone }: {
  open: boolean; purchase: PurchaseDto; onClose: () => void; onDone: (p: PurchaseDto) => void;
}) {
  const [dueDate, setDueDate] = useState("");
  const [paidNow, setPaidNow] = useState(false);
  const [invoice, setInvoice] = useState(purchase.invoiceNumber ?? "");
  const receive = useMutation({
    mutationFn: () => receivePurchase(purchase.id, {
      dueDate: dueDate ? new Date(`${dueDate}T12:00:00`).toISOString() : undefined,
      paidNow,
      invoiceNumber: invoice.trim() || undefined,
    }),
    onSuccess: onDone,
    onError: (e: Error) => toast.error(e.message),
  });

  return (
    <Dialog open={open} onOpenChange={(v) => { if (!v && !receive.isPending) onClose(); }}>
      <DialogContent className="max-w-md">
        <DialogHeader>
          <DialogTitle>Receber mercadoria</DialogTitle>
          <DialogDescription>
            Os itens entram no estoque desta loja, o custo dos produtos é atualizado e uma conta a pagar de{" "}
            {formatCurrency(purchase.totalAmount)} é lançada para o fornecedor.
          </DialogDescription>
        </DialogHeader>
        <div className="space-y-4">
          <div className="space-y-1.5">
            <Label htmlFor="receive-invoice">Nota fiscal</Label>
            <Input id="receive-invoice" value={invoice} onChange={(e) => setInvoice(e.target.value)} placeholder="Opcional" />
          </div>
          <div className="space-y-1.5">
            <Label htmlFor="receive-due">Vencimento da conta</Label>
            <Input id="receive-due" type="date" value={dueDate} onChange={(e) => setDueDate(e.target.value)} disabled={paidNow} />
            <p className="text-xs text-muted-foreground">Em branco: usa o prazo de pagamento do fornecedor.</p>
          </div>
          <label className="flex items-center gap-2 text-sm">
            <Checkbox checked={paidNow} onCheckedChange={(v) => setPaidNow(v === true)} />
            Já paga ao fornecedor (lança a conta como paga)
          </label>
        </div>
        <DialogFooter className="gap-2">
          <Button variant="outline" onClick={onClose} disabled={receive.isPending}>Voltar</Button>
          <Button onClick={() => receive.mutate()} disabled={receive.isPending}>
            {receive.isPending ? "Recebendo..." : "Confirmar recebimento"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

function CancelDialog({ open, purchase, onClose, onDone }: {
  open: boolean; purchase: PurchaseDto; onClose: () => void; onDone: (p: PurchaseDto) => void;
}) {
  const [reason, setReason] = useState("");
  const cancel = useMutation({
    mutationFn: () => cancelPurchase(purchase.id, reason.trim() || undefined),
    onSuccess: onDone,
    onError: (e: Error) => toast.error(e.message),
  });
  const received = purchase.status === "Confirmed";

  return (
    <Dialog open={open} onOpenChange={(v) => { if (!v && !cancel.isPending) onClose(); }}>
      <DialogContent className="max-w-md">
        <DialogHeader>
          <DialogTitle>Cancelar compra</DialogTitle>
          <DialogDescription>
            {received
              ? "A mercadoria sai do estoque e o financeiro é estornado: conta pendente é cancelada; conta já paga vira um valor a receber do fornecedor."
              : "O pedido é cancelado. Nada foi lançado em estoque ou financeiro."}
          </DialogDescription>
        </DialogHeader>
        <div className="space-y-1.5">
          <Label htmlFor="purchase-cancel-reason">Motivo</Label>
          <Textarea id="purchase-cancel-reason" rows={2} maxLength={500} value={reason} onChange={(e) => setReason(e.target.value)} />
        </div>
        <DialogFooter className="gap-2">
          <Button variant="outline" onClick={onClose} disabled={cancel.isPending}>Voltar</Button>
          <Button variant="destructive" onClick={() => cancel.mutate()} disabled={cancel.isPending}>
            {cancel.isPending ? "Cancelando..." : "Confirmar cancelamento"}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}
