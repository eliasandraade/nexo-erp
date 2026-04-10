import { useState, useEffect, useCallback } from "react";
import { useNavigate, useParams } from "react-router-dom";
import { useQuery } from "@tanstack/react-query";
import { PageHeader } from "@/components/shared/PageHeader";
import { SectionCard } from "@/components/shared/SectionCard";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { Tabs, TabsContent, TabsList, TabsTrigger } from "@/components/ui/tabs";
import { ArrowLeft, Save } from "lucide-react";
import { useToast } from "@/hooks/use-toast";
import { userService } from "../services/userService";
import type { UserFormInput } from "../types";
import { UserMainDataSection, UserAccessSection, UserSecuritySection } from "../components/UserFormSections";
import { UserSummaryCard } from "../components/UserSummaryCard";

const defaultForm: UserFormInput = {
  name: "", email: "", login: "", phone: "",
  role: "vendedor", company: "Andrade Systems", store: "", status: "active",
  password: "", passwordConfirm: "", requirePasswordChange: false, blocked: false, notes: "",
};

export default function UserFormPage() {
  const { id } = useParams();
  const isNew = !id;
  const navigate = useNavigate();
  const { toast } = useToast();
  const [form, setForm] = useState<UserFormInput>(defaultForm);
  const [errors, setErrors] = useState<Record<string, string>>({});
  const [saving, setSaving] = useState(false);

  const { data: stores = [] } = useQuery({ queryKey: ["user-stores"], queryFn: userService.listStores });
  const { data: existingUser, isLoading } = useQuery({
    queryKey: ["user", id],
    queryFn: () => userService.getById(id!),
    enabled: !isNew,
  });

  useEffect(() => {
    if (existingUser) {
      setForm({
        name: existingUser.name, email: existingUser.email, login: existingUser.login,
        phone: existingUser.phone, role: existingUser.role, company: existingUser.company,
        store: existingUser.store, status: existingUser.status,
        password: "", passwordConfirm: "",
        requirePasswordChange: existingUser.requirePasswordChange,
        blocked: existingUser.status === "blocked", notes: existingUser.notes,
      });
    }
  }, [existingUser]);

  const onChange = useCallback((patch: Partial<UserFormInput>) => {
    setForm((prev) => ({ ...prev, ...patch }));
    setErrors((prev) => {
      const next = { ...prev };
      Object.keys(patch).forEach((k) => delete next[k]);
      return next;
    });
  }, []);

  const validate = (): boolean => {
    const e: Record<string, string> = {};
    if (!form.name.trim()) e.name = "Nome é obrigatório";
    if (!form.email.trim() || !form.email.includes("@")) e.email = "E-mail inválido";
    if (!form.login.trim()) e.login = "Login é obrigatório";
    if (!form.role) e.role = "Perfil é obrigatório";
    if ((form.role === "gerente" || form.role === "vendedor") && (!form.store || form.store === "none")) {
      e.store = "Loja é obrigatória para este perfil";
    }
    if (isNew && !form.password) e.password = "Senha é obrigatória";
    if (form.password && form.password !== form.passwordConfirm) e.passwordConfirm = "Senhas não conferem";
    setErrors(e);
    return Object.keys(e).length === 0;
  };

  const handleSave = async () => {
    if (!validate()) {
      toast({ title: "Preencha os campos obrigatórios", variant: "destructive" });
      return;
    }
    setSaving(true);
    try {
      if (isNew) {
        await userService.create(form);
      } else {
        await userService.update(id!, form);
      }
      toast({ title: isNew ? "Usuário criado com sucesso" : "Usuário atualizado com sucesso" });
      navigate("/usuarios");
    } catch {
      toast({ title: "Erro ao salvar usuário", variant: "destructive" });
    } finally {
      setSaving(false);
    }
  };

  if (!isNew && isLoading) {
    return (
      <div className="space-y-6">
        <Skeleton className="h-8 w-48" />
        <Skeleton className="h-64 w-full" />
      </div>
    );
  }

  return (
    <div className="space-y-6">
      <PageHeader
        title={isNew ? "Novo usuário" : "Editar usuário"}
        description="Cadastre e configure os acessos do usuário."
        actions={
          <div className="flex gap-2">
            <Button variant="outline" onClick={() => navigate("/usuarios")}>
              <ArrowLeft className="h-4 w-4 mr-2" /> Cancelar
            </Button>
            <Button onClick={handleSave} disabled={saving}>
              <Save className="h-4 w-4 mr-2" /> {saving ? "Salvando..." : "Salvar"}
            </Button>
          </div>
        }
      />

      <div className="grid grid-cols-1 lg:grid-cols-3 gap-6">
        <div className="lg:col-span-2">
          <Tabs defaultValue="dados">
            <TabsList>
              <TabsTrigger value="dados">Dados principais</TabsTrigger>
              <TabsTrigger value="acesso">Acesso e vínculo</TabsTrigger>
              <TabsTrigger value="seguranca">Segurança</TabsTrigger>
            </TabsList>
            <TabsContent value="dados">
              <SectionCard title="Dados principais">
                <UserMainDataSection form={form} onChange={onChange} errors={errors} />
              </SectionCard>
            </TabsContent>
            <TabsContent value="acesso">
              <SectionCard title="Acesso e vínculo">
                <UserAccessSection form={form} onChange={onChange} errors={errors} stores={stores} />
              </SectionCard>
            </TabsContent>
            <TabsContent value="seguranca">
              <SectionCard title="Segurança">
                <UserSecuritySection form={form} onChange={onChange} errors={errors} isEdit={!isNew} />
              </SectionCard>
            </TabsContent>
          </Tabs>
        </div>

        <div>
          {!isNew && existingUser ? (
            <UserSummaryCard user={existingUser} />
          ) : (
            <SectionCard title="Resumo">
              <p className="text-sm text-muted-foreground">O resumo será exibido após salvar o usuário.</p>
            </SectionCard>
          )}
        </div>
      </div>
    </div>
  );
}
