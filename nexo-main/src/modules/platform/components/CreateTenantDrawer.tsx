import { useState } from "react";
import { X } from "lucide-react";
import { cn } from "@/lib/utils";
import { useCreateTenant } from "../hooks/usePlatformTenants";
import type { CreateTenantInput } from "../types";

const AVAILABLE_MODULES = [
  { key: "varejo",      label: "Varejo" },
  { key: "restaurante", label: "Restaurante" },
];

const BUSINESS_TYPES = [
  { value: "varejo",       label: "Varejo" },
  { value: "restaurante",  label: "Restaurante" },
  { value: "servicos",     label: "Serviços" },
  { value: "outro",        label: "Outro" },
];

interface Props {
  open: boolean;
  onClose: () => void;
  onCreated: (tenantId: string) => void;
}

export function CreateTenantDrawer({ open, onClose, onCreated }: Props) {
  const createMut = useCreateTenant();

  const [form, setForm] = useState<CreateTenantInput>({
    companyName: "",
    taxId: "",
    email: "",
    tradeName: "",
    phone: "",
    businessType: "",
    modules: [],
    adminName: "",
    adminLogin: "",
    adminPassword: "",
    adminEmail: "",
  });

  const [errors, setErrors] = useState<Partial<Record<keyof CreateTenantInput, string>>>({});

  const set = (field: keyof CreateTenantInput, value: string | string[]) =>
    setForm(f => ({ ...f, [field]: value }));

  const toggleModule = (key: string) =>
    set("modules", form.modules.includes(key)
      ? form.modules.filter(m => m !== key)
      : [...form.modules, key]);

  const validate = (): boolean => {
    const e: typeof errors = {};
    if (!form.companyName.trim()) e.companyName = "Obrigatório";
    if (!form.taxId.trim()) e.taxId = "Obrigatório";
    if (!form.email.trim()) e.email = "Obrigatório";
    if (form.modules.length === 0) e.modules = "Selecione pelo menos um módulo.";
    if (!form.adminName.trim()) e.adminName = "Obrigatório";
    if (!form.adminLogin.trim()) e.adminLogin = "Obrigatório";
    if (!form.adminPassword.trim()) e.adminPassword = "Obrigatório";
    if (form.adminPassword.length < 6) e.adminPassword = "Mínimo 6 caracteres";
    setErrors(e);
    return Object.keys(e).length === 0;
  };

  const handleSubmit = async () => {
    if (!validate()) return;
    try {
      const result = await createMut.mutateAsync(form);
      onCreated(result.id);
      onClose();
      setForm({
        companyName: "", taxId: "", email: "", tradeName: "", phone: "",
        businessType: "", modules: [], adminName: "", adminLogin: "",
        adminPassword: "", adminEmail: "",
      });
    } catch (err: unknown) {
      const msg = err instanceof Error ? err.message : "Erro ao criar tenant.";
      setErrors(e => ({ ...e, email: msg }));
    }
  };

  if (!open) return null;

  return (
    <div className="fixed inset-0 z-50 flex">
      {/* Backdrop */}
      <div className="flex-1 bg-black/40" onClick={onClose} />

      {/* Panel */}
      <div className="w-full max-w-md bg-background border-l border-border flex flex-col overflow-hidden">

        {/* Header */}
        <div className="flex items-center justify-between px-5 py-4 border-b border-border shrink-0">
          <h2 className="text-base font-semibold text-foreground">Novo cliente</h2>
          <button onClick={onClose} className="p-1.5 rounded-md hover:bg-muted transition-colors">
            <X className="h-4 w-4 text-muted-foreground" />
          </button>
        </div>

        {/* Body */}
        <div className="flex-1 overflow-y-auto px-5 py-5 space-y-6">

          {/* Empresa */}
          <section>
            <p className="text-xs font-semibold text-muted-foreground uppercase tracking-wide mb-3">Empresa</p>
            <div className="space-y-3">
              <Field label="Razão social *" error={errors.companyName}>
                <input className={input(!!errors.companyName)} value={form.companyName}
                  onChange={e => set("companyName", e.target.value)} placeholder="Ex: Loja do João LTDA" />
              </Field>
              <Field label="Nome fantasia">
                <input className={input(false)} value={form.tradeName}
                  onChange={e => set("tradeName", e.target.value)} placeholder="Ex: Loja do João" />
              </Field>
              <div className="grid grid-cols-2 gap-3">
                <Field label="CNPJ / CPF *" error={errors.taxId}>
                  <input className={input(!!errors.taxId)} value={form.taxId}
                    onChange={e => set("taxId", e.target.value)} placeholder="00.000.000/0001-00" />
                </Field>
                <Field label="Telefone">
                  <input className={input(false)} value={form.phone}
                    onChange={e => set("phone", e.target.value)} placeholder="(85) 99999-9999" />
                </Field>
              </div>
              <Field label="E-mail da empresa *" error={errors.email}>
                <input type="email" className={input(!!errors.email)} value={form.email}
                  onChange={e => set("email", e.target.value)} placeholder="contato@empresa.com" />
              </Field>
              <Field label="Segmento">
                <select className={input(false)} value={form.businessType}
                  onChange={e => set("businessType", e.target.value)}>
                  <option value="">Selecionar...</option>
                  {BUSINESS_TYPES.map(b => (
                    <option key={b.value} value={b.value}>{b.label}</option>
                  ))}
                </select>
              </Field>
            </div>
          </section>

          {/* Módulos */}
          <section>
            <p className="text-xs font-semibold text-muted-foreground uppercase tracking-wide mb-3">Módulos *</p>
            <div className="flex flex-wrap gap-2">
              {AVAILABLE_MODULES.map(m => (
                <button
                  key={m.key}
                  type="button"
                  onClick={() => toggleModule(m.key)}
                  className={cn(
                    "px-4 py-2 rounded-lg text-sm border transition-colors",
                    form.modules.includes(m.key)
                      ? "border-primary bg-primary/10 text-primary font-medium"
                      : "border-border text-muted-foreground hover:border-muted-foreground/60"
                  )}
                >
                  {m.label}
                </button>
              ))}
            </div>
            {errors.modules && <p className="text-xs text-destructive mt-1">{errors.modules}</p>}
            <p className="text-xs text-muted-foreground mt-2">Módulos concedidos imediatamente via admin grant.</p>
          </section>

          {/* Admin */}
          <section>
            <p className="text-xs font-semibold text-muted-foreground uppercase tracking-wide mb-3">Usuário admin inicial</p>
            <div className="space-y-3">
              <Field label="Nome completo *" error={errors.adminName}>
                <input className={input(!!errors.adminName)} value={form.adminName}
                  onChange={e => set("adminName", e.target.value)} placeholder="Nome do responsável" />
              </Field>
              <div className="grid grid-cols-2 gap-3">
                <Field label="Login *" error={errors.adminLogin}>
                  <input className={input(!!errors.adminLogin)} value={form.adminLogin}
                    onChange={e => set("adminLogin", e.target.value)} placeholder="usuario" />
                </Field>
                <Field label="Senha *" error={errors.adminPassword}>
                  <input type="password" className={input(!!errors.adminPassword)} value={form.adminPassword}
                    onChange={e => set("adminPassword", e.target.value)} placeholder="••••••" />
                </Field>
              </div>
              <Field label="E-mail do admin">
                <input type="email" className={input(false)} value={form.adminEmail}
                  onChange={e => set("adminEmail", e.target.value)} placeholder="(padrão: e-mail da empresa)" />
              </Field>
              <p className="text-xs text-muted-foreground">
                O admin receberá o role Diretoria e será solicitado a trocar a senha no primeiro acesso.
              </p>
            </div>
          </section>

        </div>

        {/* Footer */}
        <div className="px-5 py-4 border-t border-border shrink-0 flex gap-3">
          <button
            onClick={onClose}
            className="flex-1 h-10 rounded-lg border border-border text-sm text-muted-foreground hover:bg-muted transition-colors"
          >
            Cancelar
          </button>
          <button
            onClick={handleSubmit}
            disabled={createMut.isPending}
            className="flex-1 h-10 rounded-lg bg-primary text-primary-foreground text-sm font-medium hover:bg-primary/90 transition-colors disabled:opacity-50"
          >
            {createMut.isPending ? "Criando..." : "Criar cliente"}
          </button>
        </div>
      </div>
    </div>
  );
}

// ─── helpers ──────────────────────────────────────────────────────────────────

function Field({ label, error, children }: { label: string; error?: string; children: React.ReactNode }) {
  return (
    <div>
      <label className="block text-xs font-medium text-foreground mb-1">{label}</label>
      {children}
      {error && <p className="text-xs text-destructive mt-1">{error}</p>}
    </div>
  );
}

function input(hasError: boolean) {
  return cn(
    "w-full h-9 px-3 rounded-lg border bg-muted/30 text-sm placeholder:text-muted-foreground focus:outline-none focus:ring-2",
    hasError
      ? "border-destructive/60 focus:ring-destructive/30"
      : "border-border focus:ring-primary/20"
  );
}
