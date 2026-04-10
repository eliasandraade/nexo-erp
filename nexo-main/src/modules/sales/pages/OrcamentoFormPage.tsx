import { useState, useEffect } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { ArrowLeft, Save } from "lucide-react";
import { toast } from "sonner";
import { PageHeader } from "@/components/shared/PageHeader";
import { SectionCard } from "@/components/shared/SectionCard";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { Input } from "@/components/ui/input";
import { Textarea } from "@/components/ui/textarea";
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select";
import { quotationService } from "../services/quotationService";
import { QuotationItemsEditor } from "../components/QuotationItemsEditor";
import { QuotationTotalsCard } from "../components/QuotationTotalsCard";
import { QuotationStatusBadge } from "../components/QuotationStatusBadge";
import { customerService } from "@/modules/customers/services/customerService";
import { useAuth } from "@/modules/auth/context/AuthContext";
import { formatDateTime } from "@/lib/formatters";
import type { QuotationFormInput, QuotationItem, QuotationStatus } from "../types/quotation";

const EMPTY_FORM: QuotationFormInput = {
  operator: "",
  customerId: "",
  customerName: "",
  status: "draft",
  notes: "",
  items: [],
};

export default function OrcamentoFormPage() {
  const { id } = useParams();
  const isEdit = Boolean(id);
  const navigate = useNavigate();
  const queryClient = useQueryClient();
  const { session } = useAuth();

  const [form, setForm] = useState<QuotationFormInput>({
    ...EMPTY_FORM,
    operator: session?.name ?? "",
  });

  // Load existing quotation in edit mode
  const { data: existing, isLoading: loadingExisting } = useQuery({
    queryKey: ["quotation", id],
    queryFn: () => quotationService.getById(id!),
    enabled: isEdit,
  });

  // Customer list for selection
  const { data: customers = [] } = useQuery({
    queryKey: ["customers"],
    queryFn: () => customerService.list(),
    staleTime: 60_000,
  });

  useEffect(() => {
    if (existing) {
      setForm({
        operator: existing.operator,
        customerId: existing.customerId ?? "",
        customerName: existing.customerName ?? "",
        status: existing.status,
        notes: existing.notes,
        items: existing.items,
      });
    }
  }, [existing]);

  const createMutation = useMutation({
    mutationFn: (input: QuotationFormInput) => quotationService.create(input),
    onSuccess: (created) => {
      queryClient.invalidateQueries({ queryKey: ["quotations"] });
      queryClient.invalidateQueries({ queryKey: ["quotation-operators"] });
      toast.success("Orçamento criado com sucesso.");
      navigate(`/orcamentos/${created.id}`);
    },
    onError: () => toast.error("Erro ao criar orçamento."),
  });

  const updateMutation = useMutation({
    mutationFn: (input: Partial<QuotationFormInput>) =>
      quotationService.update(id!, input),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["quotations"] });
      queryClient.invalidateQueries({ queryKey: ["quotation", id] });
      toast.success("Orçamento atualizado com sucesso.");
    },
    onError: () => toast.error("Erro ao atualizar orçamento."),
  });

  const isSaving = createMutation.isPending || updateMutation.isPending;

  function handleSubmit(e: React.FormEvent) {
    e.preventDefault();
    if (form.items.length === 0) {
      toast.error("Adicione ao menos um item ao orçamento.");
      return;
    }
    if (isEdit) {
      updateMutation.mutate(form);
    } else {
      createMutation.mutate(form);
    }
  }

  function handleCustomerChange(customerId: string) {
    if (customerId === "__none__") {
      setForm((f) => ({ ...f, customerId: "", customerName: "" }));
      return;
    }
    const customer = customers.find((c) => c.id === customerId);
    setForm((f) => ({
      ...f,
      customerId,
      customerName: customer?.name ?? "",
    }));
  }

  if (isEdit && loadingExisting) {
    return (
      <div className="space-y-6">
        <Skeleton className="h-10 w-72" />
        <Skeleton className="h-96 w-full" />
      </div>
    );
  }

  const title = isEdit ? `Orçamento ${id}` : "Novo orçamento";
  const description = isEdit && existing
    ? `Criado em ${formatDateTime(existing.createdAt)} por ${existing.createdBy}`
    : "Preencha os dados e adicione os itens do orçamento.";

  return (
    <form onSubmit={handleSubmit}>
      <div className="space-y-6">
        <PageHeader
          title={title}
          description={description}
          actions={
            <div className="flex items-center gap-2">
              {isEdit && existing && (
                <QuotationStatusBadge status={existing.status} />
              )}
              <Button
                type="button"
                variant="outline"
                onClick={() => navigate("/orcamentos")}
                className="gap-2"
              >
                <ArrowLeft className="h-4 w-4" />
                Voltar
              </Button>
              <Button type="submit" disabled={isSaving} className="gap-2">
                <Save className="h-4 w-4" />
                {isSaving ? "Salvando..." : "Salvar"}
              </Button>
            </div>
          }
        />

        <div className="grid grid-cols-1 lg:grid-cols-[1fr_280px] gap-6 items-start">
          {/* Main content — tabs */}
          <Tabs defaultValue="header" className="space-y-4">
            <TabsList>
              <TabsTrigger value="header">Cabeçalho</TabsTrigger>
              <TabsTrigger value="items">
                Itens
                {form.items.length > 0 && (
                  <span className="ml-1.5 text-xs bg-primary/20 text-primary rounded-full px-1.5 py-0.5">
                    {form.items.length}
                  </span>
                )}
              </TabsTrigger>
              {isEdit && <TabsTrigger value="summary">Resumo</TabsTrigger>}
            </TabsList>

            {/* Cabeçalho tab */}
            <TabsContent value="header">
              <SectionCard title="Dados do orçamento">
                <div className="grid grid-cols-1 sm:grid-cols-2 gap-4">
                  {/* Operator */}
                  <div className="space-y-1.5">
                    <label className="text-sm font-medium">Operador</label>
                    <Input
                      value={form.operator}
                      onChange={(e) =>
                        setForm((f) => ({ ...f, operator: e.target.value }))
                      }
                      placeholder="Nome do operador"
                    />
                  </div>

                  {/* Status */}
                  <div className="space-y-1.5">
                    <label className="text-sm font-medium">Status</label>
                    <Select
                      value={form.status}
                      onValueChange={(v) =>
                        setForm((f) => ({ ...f, status: v as QuotationStatus }))
                      }
                    >
                      <SelectTrigger>
                        <SelectValue />
                      </SelectTrigger>
                      <SelectContent>
                        <SelectItem value="draft">Rascunho</SelectItem>
                        <SelectItem value="sent">Enviado</SelectItem>
                        <SelectItem value="approved">Aprovado</SelectItem>
                        <SelectItem value="expired">Expirado</SelectItem>
                        <SelectItem value="converted">Convertido</SelectItem>
                      </SelectContent>
                    </Select>
                  </div>

                  {/* Customer */}
                  <div className="space-y-1.5 sm:col-span-2">
                    <label className="text-sm font-medium">
                      Cliente{" "}
                      <span className="text-muted-foreground font-normal">
                        (opcional)
                      </span>
                    </label>
                    <Select
                      value={form.customerId || "__none__"}
                      onValueChange={handleCustomerChange}
                    >
                      <SelectTrigger>
                        <SelectValue placeholder="Selecionar cliente..." />
                      </SelectTrigger>
                      <SelectContent>
                        <SelectItem value="__none__">Sem cliente</SelectItem>
                        {customers.map((c) => (
                          <SelectItem key={c.id} value={c.id}>
                            {c.name}
                            {c.document && (
                              <span className="ml-2 text-muted-foreground text-xs">
                                {c.document}
                              </span>
                            )}
                          </SelectItem>
                        ))}
                      </SelectContent>
                    </Select>
                  </div>

                  {/* Notes */}
                  <div className="space-y-1.5 sm:col-span-2">
                    <label className="text-sm font-medium">
                      Observações{" "}
                      <span className="text-muted-foreground font-normal">
                        (opcional)
                      </span>
                    </label>
                    <Textarea
                      value={form.notes}
                      onChange={(e) =>
                        setForm((f) => ({ ...f, notes: e.target.value }))
                      }
                      placeholder="Condições comerciais, prazo de validade, observações gerais..."
                      rows={4}
                    />
                  </div>
                </div>
              </SectionCard>
            </TabsContent>

            {/* Itens tab */}
            <TabsContent value="items">
              <SectionCard title="Itens do orçamento">
                <QuotationItemsEditor
                  items={form.items}
                  onChange={(items: QuotationItem[]) =>
                    setForm((f) => ({ ...f, items }))
                  }
                />
              </SectionCard>
            </TabsContent>

            {/* Resumo tab (edit only) */}
            {isEdit && existing && (
              <TabsContent value="summary">
                <SectionCard title="Informações do orçamento">
                  <dl className="grid grid-cols-1 sm:grid-cols-2 gap-4 text-sm">
                    <div>
                      <dt className="text-muted-foreground text-xs">ID</dt>
                      <dd className="font-mono mt-0.5">{existing.id}</dd>
                    </div>
                    <div>
                      <dt className="text-muted-foreground text-xs">Status</dt>
                      <dd className="mt-1">
                        <QuotationStatusBadge status={existing.status} />
                      </dd>
                    </div>
                    <div>
                      <dt className="text-muted-foreground text-xs">Criado em</dt>
                      <dd className="mt-0.5">{formatDateTime(existing.createdAt)}</dd>
                    </div>
                    <div>
                      <dt className="text-muted-foreground text-xs">
                        Última atualização
                      </dt>
                      <dd className="mt-0.5">{formatDateTime(existing.updatedAt)}</dd>
                    </div>
                    <div>
                      <dt className="text-muted-foreground text-xs">Criado por</dt>
                      <dd className="mt-0.5">{existing.createdBy}</dd>
                    </div>
                    <div>
                      <dt className="text-muted-foreground text-xs">Operador</dt>
                      <dd className="mt-0.5">{existing.operator}</dd>
                    </div>
                    <div>
                      <dt className="text-muted-foreground text-xs">Cliente</dt>
                      <dd className="mt-0.5">
                        {existing.customerName ?? (
                          <span className="text-muted-foreground italic">
                            Sem cliente
                          </span>
                        )}
                      </dd>
                    </div>
                    {existing.convertedToSaleId && (
                      <div>
                        <dt className="text-muted-foreground text-xs">
                          Venda gerada
                        </dt>
                        <dd className="font-mono mt-0.5">
                          {existing.convertedToSaleId}
                        </dd>
                      </div>
                    )}
                    {existing.notes && (
                      <div className="sm:col-span-2">
                        <dt className="text-muted-foreground text-xs">
                          Observações
                        </dt>
                        <dd className="mt-0.5 whitespace-pre-line">
                          {existing.notes}
                        </dd>
                      </div>
                    )}
                  </dl>
                </SectionCard>
              </TabsContent>
            )}
          </Tabs>

          {/* Sidebar — totals */}
          <QuotationTotalsCard items={form.items} />
        </div>
      </div>
    </form>
  );
}
