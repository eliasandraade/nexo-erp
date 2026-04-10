import { useState, useEffect } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { ArrowLeft, Save } from "lucide-react";
import { toast } from "sonner";
import { PageHeader } from "@/components/shared/PageHeader";
import { SectionCard } from "@/components/shared/SectionCard";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import {
  useSupplier,
  useCreateSupplier,
  useUpdateSupplier,
  useSetSupplierActive,
} from "../hooks/use-suppliers";
import { dtoToFormInput, emptySupplierForm } from "../types";
import { SupplierMainDataSection } from "../components/SupplierMainDataSection";
import { SupplierContactSection } from "../components/SupplierContactSection";
import { SupplierCommercialSection } from "../components/SupplierCommercialSection";
import { SupplierSummaryCard } from "../components/SupplierSummaryCard";
import type { SupplierFormInput } from "../types";

function validate(form: SupplierFormInput): string | null {
  if (!form.name.trim()) return "Nome / Razão social é obrigatório.";
  if (!form.documentNumber.trim()) return "Número do documento é obrigatório.";
  if (form.email && !form.email.includes("@")) return "E-mail inválido.";
  return null;
}

export default function SupplierFormPage() {
  const { id } = useParams<{ id: string }>();
  const isEdit = Boolean(id);
  const navigate = useNavigate();

  const { data: existing, isLoading } = useSupplier(id);
  const createMutation = useCreateSupplier();
  const updateMutation = useUpdateSupplier(id!);
  const setActiveMutation = useSetSupplierActive(id!);

  const [form, setForm] = useState<SupplierFormInput>(emptySupplierForm);
  const [initialIsActive, setInitialIsActive] = useState<boolean | null>(null);

  useEffect(() => {
    if (!existing) return;
    const f = dtoToFormInput(existing);
    setForm(f);
    setInitialIsActive(existing.isActive);
  }, [existing]);

  const onChange = (field: keyof SupplierFormInput, value: unknown) => {
    setForm((prev) => ({ ...prev, [field]: value }));
  };

  const handleSave = async () => {
    const error = validate(form);
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
        toast.success("Fornecedor atualizado com sucesso.");
      } else {
        await createMutation.mutateAsync(form);
        toast.success("Fornecedor cadastrado com sucesso.");
      }
      navigate("/fornecedores");
    } catch {
      toast.error("Não foi possível salvar o fornecedor. Tente novamente.");
    }
  };

  const isPending =
    createMutation.isPending || updateMutation.isPending || setActiveMutation.isPending;

  if (isEdit && isLoading) {
    return (
      <div className="space-y-6">
        <div className="flex items-start justify-between">
          <div className="space-y-1.5">
            <Skeleton className="h-6 w-52" />
            <Skeleton className="h-4 w-72" />
          </div>
          <div className="flex gap-2">
            <Skeleton className="h-9 w-24" />
            <Skeleton className="h-9 w-20" />
          </div>
        </div>
        <Skeleton className="h-[440px] w-full rounded-lg" />
      </div>
    );
  }

  return (
    <div className="space-y-6">
      <PageHeader
        title={isEdit ? "Editar fornecedor" : "Novo fornecedor"}
        description="Cadastre e configure as informações do fornecedor."
        actions={
          <div className="flex items-center gap-2">
            <Button variant="outline" size="sm" onClick={() => navigate("/fornecedores")}>
              <ArrowLeft className="h-4 w-4 mr-1.5" />
              Cancelar
            </Button>
            <Button size="sm" onClick={handleSave} disabled={isPending}>
              <Save className="h-4 w-4 mr-1.5" />
              {isPending ? "Salvando..." : "Salvar"}
            </Button>
          </div>
        }
      />

      <div className="grid grid-cols-1 lg:grid-cols-[1fr_280px] gap-6 items-start">
        <Tabs defaultValue="main" className="space-y-4">
          <TabsList>
            <TabsTrigger value="main">Dados principais</TabsTrigger>
            <TabsTrigger value="contact">Contato e endereço</TabsTrigger>
            <TabsTrigger value="commercial">Comercial</TabsTrigger>
          </TabsList>

          <TabsContent value="main">
            <SectionCard title="Dados principais">
              <SupplierMainDataSection data={form} onChange={onChange} isEdit={isEdit} />
            </SectionCard>
          </TabsContent>

          <TabsContent value="contact">
            <SectionCard title="Contato e endereço">
              <SupplierContactSection data={form} onChange={onChange} />
            </SectionCard>
          </TabsContent>

          <TabsContent value="commercial">
            <SectionCard title="Informações comerciais">
              <SupplierCommercialSection data={form} onChange={onChange} />
            </SectionCard>
          </TabsContent>
        </Tabs>

        {isEdit && <SupplierSummaryCard supplier={existing ?? null} />}
      </div>
    </div>
  );
}
