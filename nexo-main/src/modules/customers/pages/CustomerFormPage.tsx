import { useState, useEffect } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { PageHeader } from "@/components/shared/PageHeader";
import { SectionCard } from "@/components/shared/SectionCard";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { ArrowLeft, Save } from "lucide-react";
import { toast } from "sonner";
import {
  useCustomer,
  useCreateCustomer,
  useUpdateCustomer,
  useSetCustomerActive,
} from "../hooks/use-customers";
import { dtoToFormInput, emptyCustomerForm } from "../types";
import { CustomerMainDataSection } from "../components/CustomerMainDataSection";
import { CustomerContactSection } from "../components/CustomerContactSection";
import { CustomerCommercialSection } from "../components/CustomerCommercialSection";
import { CustomerSummaryCard } from "../components/CustomerSummaryCard";
import type { CustomerFormInput } from "../types";

export default function CustomerFormPage() {
  const { id } = useParams();
  const isEdit = Boolean(id);
  const navigate = useNavigate();

  const { data: existing, isLoading } = useCustomer(id);
  const createMutation = useCreateCustomer();
  const updateMutation = useUpdateCustomer(id!);
  const setActiveMutation = useSetCustomerActive(id!);

  const [form, setForm] = useState<CustomerFormInput>(emptyCustomerForm);
  const [initialIsActive, setInitialIsActive] = useState<boolean | null>(null);

  useEffect(() => {
    if (existing) {
      const f = dtoToFormInput(existing);
      setForm(f);
      setInitialIsActive(existing.isActive);
    }
  }, [existing]);

  const onChange = (field: keyof CustomerFormInput, value: unknown) => {
    setForm((prev) => ({ ...prev, [field]: value }));
  };

  const validate = (): string | null => {
    if (!form.name.trim()) return "Nome é obrigatório.";
    if (!form.documentNumber.trim()) return "Número do documento é obrigatório.";
    if (form.email && !form.email.includes("@")) return "E-mail inválido.";
    return null;
  };

  const handleSave = async () => {
    const error = validate();
    if (error) {
      toast.error(error);
      return;
    }

    try {
      if (isEdit) {
        await updateMutation.mutateAsync(form);
        if (initialIsActive !== null && form.isActive !== initialIsActive) {
          await setActiveMutation.mutateAsync(form.isActive);
        }
        toast.success("Cliente atualizado com sucesso.");
      } else {
        await createMutation.mutateAsync(form);
        toast.success("Cliente cadastrado com sucesso.");
      }
      navigate("/clientes");
    } catch {
      toast.error("Não foi possível salvar o cliente.");
    }
  };

  const isPending =
    createMutation.isPending || updateMutation.isPending || setActiveMutation.isPending;

  if (isEdit && isLoading) {
    return (
      <div className="space-y-6">
        <Skeleton className="h-8 w-64" />
        <Skeleton className="h-[400px] w-full" />
      </div>
    );
  }

  return (
    <div className="space-y-6">
      <PageHeader
        title={isEdit ? "Editar cliente" : "Novo cliente"}
        description="Cadastre e configure as informações do cliente."
        actions={
          <div className="flex items-center gap-2">
            <Button variant="outline" onClick={() => navigate("/clientes")}>
              <ArrowLeft className="h-4 w-4 mr-2" /> Cancelar
            </Button>
            <Button onClick={handleSave} disabled={isPending}>
              <Save className="h-4 w-4 mr-2" /> {isPending ? "Salvando..." : "Salvar"}
            </Button>
          </div>
        }
      />

      <div className="grid grid-cols-1 lg:grid-cols-[1fr_280px] gap-6">
        <Tabs defaultValue="main" className="space-y-4">
          <TabsList>
            <TabsTrigger value="main">Dados principais</TabsTrigger>
            <TabsTrigger value="contact">Contato e endereço</TabsTrigger>
            <TabsTrigger value="commercial">Comercial</TabsTrigger>
          </TabsList>
          <TabsContent value="main">
            <SectionCard title="Dados principais">
              <CustomerMainDataSection data={form} onChange={onChange} isEdit={isEdit} />
            </SectionCard>
          </TabsContent>
          <TabsContent value="contact">
            <SectionCard title="Contato e endereço">
              <CustomerContactSection data={form} onChange={onChange} />
            </SectionCard>
          </TabsContent>
          <TabsContent value="commercial">
            <SectionCard title="Informações comerciais">
              <CustomerCommercialSection data={form} onChange={onChange} />
            </SectionCard>
          </TabsContent>
        </Tabs>

        {isEdit && <CustomerSummaryCard customer={existing ?? null} />}
      </div>
    </div>
  );
}
