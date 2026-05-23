import { useState, useEffect } from "react";
import { useQuery, useMutation, useQueryClient } from "@tanstack/react-query";
import { Loader2, Plug } from "lucide-react";
import { toast } from "sonner";

import { PageHeader } from "@/components/shared/PageHeader";
import { SectionCard } from "@/components/shared/SectionCard";
import { EmptyState } from "@/components/shared/EmptyState";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";
import { Skeleton } from "@/components/ui/skeleton";
import { Tabs, TabsList, TabsTrigger, TabsContent } from "@/components/ui/tabs";
import {
  Select,
  SelectTrigger,
  SelectValue,
  SelectContent,
  SelectItem,
} from "@/components/ui/select";

import { settingsService } from "../services/settingsService";
import { defaultSettings } from "../data/defaultSettings";
import { minStockBehaviorLabels } from "../types";
import type { AppSettings } from "../types";
import { SettingsBooleanField } from "../components/SettingsBooleanField";
import { SettingsNumberField } from "../components/SettingsNumberField";
import { userService } from "@/modules/users/services/userService";

// ---------------------------------------------------------------------------
// Local helpers
// ---------------------------------------------------------------------------

function SettingsField({
  label,
  description,
  children,
}: {
  label: string;
  description?: string;
  children: React.ReactNode;
}) {
  return (
    <div className="space-y-1.5">
      <Label className="text-sm font-medium">{label}</Label>
      {description && (
        <p className="text-xs text-muted-foreground">{description}</p>
      )}
      {children}
    </div>
  );
}

function SaveFooter({
  onSave,
  saving,
}: {
  onSave: () => void;
  saving: boolean;
}) {
  return (
    <div className="flex justify-end pt-4 mt-2 border-t border-border">
      <Button size="sm" onClick={onSave} disabled={saving}>
        {saving && <Loader2 className="h-3.5 w-3.5 mr-1.5 animate-spin" />}
        Salvar alterações
      </Button>
    </div>
  );
}

function SettingsSkeleton() {
  return (
    <div className="space-y-6">
      <div className="space-y-1">
        <Skeleton className="h-6 w-40" />
        <Skeleton className="h-4 w-72" />
      </div>
      <Skeleton className="h-9 w-full max-w-lg" />
      <div className="bg-card rounded-xl border border-border p-5 space-y-4">
        {Array.from({ length: 5 }).map((_, i) => (
          <Skeleton key={i} className="h-9 w-full" />
        ))}
      </div>
    </div>
  );
}

// ---------------------------------------------------------------------------
// Page
// ---------------------------------------------------------------------------

export default function ConfiguracoesPage() {
  const queryClient = useQueryClient();

  const { data: settings, isLoading } = useQuery({
    queryKey: ["settings"],
    queryFn: settingsService.getSettings,
    staleTime: 60_000,
  });

  const { data: stores = [] } = useQuery({
    queryKey: ["user-stores"],
    queryFn: userService.listStores,
    staleTime: 60_000,
  });

  // ── Per-section form state ──────────────────────────────────────────────
  const [companyForm, setCompanyForm] = useState(defaultSettings.company);
  const [operationForm, setOperationForm] = useState(defaultSettings.operation);
  const [inventoryForm, setInventoryForm] = useState(defaultSettings.inventory);
  const [commissionForm, setCommissionForm] = useState(defaultSettings.commission);
  const [posForm, setPosForm] = useState(defaultSettings.pos);
  const [systemForm, setSystemForm] = useState(defaultSettings.system);

  // Sync all form sections when settings load or reload after a successful save
  useEffect(() => {
    if (!settings) return;
    setCompanyForm(settings.company);
    setOperationForm(settings.operation);
    setInventoryForm(settings.inventory);
    setCommissionForm(settings.commission);
    setPosForm(settings.pos);
    setSystemForm(settings.system);
  }, [settings]);

  // ── Shared mutation ─────────────────────────────────────────────────────
  const [savingSection, setSavingSection] = useState<string | null>(null);

  const saveMutation = useMutation({
    mutationFn: (partial: Partial<AppSettings>) =>
      settingsService.updateSettings(partial),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: ["settings"] });
      toast.success("Configurações salvas.");
      setSavingSection(null);
    },
    onError: () => {
      toast.error("Erro ao salvar configurações.");
      setSavingSection(null);
    },
  });

  function save(section: string, partial: Partial<AppSettings>) {
    setSavingSection(section);
    saveMutation.mutate(partial);
  }

  const isSaving = (s: string) =>
    saveMutation.isPending && savingSection === s;

  if (isLoading) return <SettingsSkeleton />;

  return (
    <div className="space-y-6">
      <PageHeader
        title="Configurações"
        description="Defina parâmetros gerais do sistema e da operação."
      />

      <Tabs defaultValue="empresa">
        <TabsList className="flex-wrap h-auto gap-1">
          <TabsTrigger value="empresa">Empresa</TabsTrigger>
          <TabsTrigger value="operacao">Operação</TabsTrigger>
          <TabsTrigger value="estoque">Estoque</TabsTrigger>
          <TabsTrigger value="comissoes">Comissões</TabsTrigger>
          <TabsTrigger value="pdv">PDV</TabsTrigger>
          <TabsTrigger value="sistema">Sistema</TabsTrigger>
        </TabsList>

        {/* ── Empresa ────────────────────────────────────────────────────── */}
        <TabsContent value="empresa" className="mt-6 space-y-4">
          <SectionCard title="Dados da empresa">
            <div className="grid sm:grid-cols-2 gap-4 mb-2">
              <SettingsField label="Razão social">
                <Input
                  value={companyForm.name}
                  onChange={(e) =>
                    setCompanyForm((f) => ({ ...f, name: e.target.value }))
                  }
                />
              </SettingsField>
              <SettingsField label="Nome fantasia">
                <Input
                  value={companyForm.tradeName}
                  onChange={(e) =>
                    setCompanyForm((f) => ({ ...f, tradeName: e.target.value }))
                  }
                />
              </SettingsField>
              <SettingsField label="CNPJ">
                <Input
                  value={companyForm.cnpj}
                  onChange={(e) =>
                    setCompanyForm((f) => ({ ...f, cnpj: e.target.value }))
                  }
                  placeholder="00.000.000/0001-00"
                />
              </SettingsField>
              <SettingsField label="Telefone">
                <Input
                  value={companyForm.phone}
                  onChange={(e) =>
                    setCompanyForm((f) => ({ ...f, phone: e.target.value }))
                  }
                  placeholder="(00) 0000-0000"
                />
              </SettingsField>
              <SettingsField
                label="E-mail"
                description="Endereço de contato principal."
              >
                <Input
                  type="email"
                  value={companyForm.email}
                  onChange={(e) =>
                    setCompanyForm((f) => ({ ...f, email: e.target.value }))
                  }
                  placeholder="contato@empresa.com.br"
                />
              </SettingsField>
            </div>
            <SaveFooter
              onSave={() => {
                if (!companyForm.name.trim()) {
                  toast.error("Razão social é obrigatória.");
                  return;
                }
                save("empresa", { company: companyForm });
              }}
              saving={isSaving("empresa")}
            />
          </SectionCard>
        </TabsContent>

        {/* ── Operação ───────────────────────────────────────────────────── */}
        <TabsContent value="operacao" className="mt-6 space-y-4">
          <SectionCard title="Loja e operador padrão">
            <div className="space-y-4 mb-2">
              <SettingsField
                label="Loja padrão"
                description="Loja pré-selecionada ao abrir o sistema."
              >
                <Select
                  value={operationForm.defaultStore}
                  onValueChange={(v) =>
                    setOperationForm((f) => ({ ...f, defaultStore: v }))
                  }
                >
                  <SelectTrigger>
                    <SelectValue placeholder="Selecione uma loja" />
                  </SelectTrigger>
                  <SelectContent>
                    {stores.map((s) => (
                      <SelectItem key={s} value={s}>
                        {s}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </SettingsField>
              <SettingsField
                label="Operador padrão"
                description="Login do operador sugerido ao abrir o PDV. Deixe em branco para não sugerir."
              >
                <Input
                  value={operationForm.defaultOperator}
                  onChange={(e) =>
                    setOperationForm((f) => ({
                      ...f,
                      defaultOperator: e.target.value,
                    }))
                  }
                  placeholder="login do operador"
                />
              </SettingsField>
            </div>
            <SaveFooter
              onSave={() => save("operacao", { operation: operationForm })}
              saving={isSaving("operacao")}
            />
          </SectionCard>
        </TabsContent>

        {/* ── Estoque ─────────────────────────────────────────────────────── */}
        <TabsContent value="estoque" className="mt-6 space-y-4">
          <SectionCard title="Alertas de estoque">
            <div className="mb-2">
              <SettingsNumberField
                label="Alertar produtos sem giro"
                description="Gera alerta para produtos sem movimentação no período definido."
                value={inventoryForm.noMovementAlertDays}
                onChange={(v) =>
                  setInventoryForm((f) => ({
                    ...f,
                    noMovementAlertDays: Math.max(1, v),
                  }))
                }
                min={1}
                max={365}
                unit="dias"
                inputWidth="w-20"
              />
              <SettingsBooleanField
                label="Alertar estoque baixo"
                description="Exibe alertas quando o estoque atingir o mínimo configurado por produto."
                checked={inventoryForm.enableLowStockAlerts}
                onCheckedChange={(v) =>
                  setInventoryForm((f) => ({ ...f, enableLowStockAlerts: v }))
                }
              />
              <SettingsBooleanField
                label="Alertar estoque zerado"
                description="Exibe alertas críticos quando o estoque chegar a zero."
                checked={inventoryForm.enableZeroStockAlerts}
                onCheckedChange={(v) =>
                  setInventoryForm((f) => ({ ...f, enableZeroStockAlerts: v }))
                }
              />
              <SettingsBooleanField
                label="Alertar produto com alto giro e estoque baixo"
                description="Destaca produtos com giro elevado cujo estoque está baixo em relação à demanda."
                checked={inventoryForm.enableHighRotationAlerts}
                onCheckedChange={(v) =>
                  setInventoryForm((f) => ({
                    ...f,
                    enableHighRotationAlerts: v,
                  }))
                }
              />
            </div>
            <SaveFooter
              onSave={() =>
                save("estoque-alertas", { inventory: inventoryForm })
              }
              saving={isSaving("estoque-alertas")}
            />
          </SectionCard>

          <SectionCard title="Comportamento de estoque mínimo">
            <div className="mb-2">
              <SettingsField
                label="Ação ao atingir estoque mínimo"
                description="Define o que o sistema faz quando um produto atinge ou ultrapassa o limite mínimo."
              >
                <Select
                  value={inventoryForm.minStockBehavior}
                  onValueChange={(v) =>
                    setInventoryForm((f) => ({
                      ...f,
                      minStockBehavior: v as typeof f.minStockBehavior,
                    }))
                  }
                >
                  <SelectTrigger>
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    {(
                      Object.entries(minStockBehaviorLabels) as [
                        string,
                        string,
                      ][]
                    ).map(([value, label]) => (
                      <SelectItem key={value} value={value}>
                        {label}
                      </SelectItem>
                    ))}
                  </SelectContent>
                </Select>
              </SettingsField>
            </div>
            <SaveFooter
              onSave={() =>
                save("estoque-comportamento", { inventory: inventoryForm })
              }
              saving={isSaving("estoque-comportamento")}
            />
          </SectionCard>
        </TabsContent>

        {/* ── Comissões ──────────────────────────────────────────────────── */}
        <TabsContent value="comissoes" className="mt-6 space-y-4">
          <SectionCard title="Parâmetros de comissão">
            <div className="mb-2">
              <SettingsNumberField
                label="Taxa de comissão padrão"
                description="Aplicada a produtos sem taxa própria definida."
                value={commissionForm.defaultCommissionRate}
                onChange={(v) =>
                  setCommissionForm((f) => ({
                    ...f,
                    defaultCommissionRate: Math.min(100, Math.max(0, v)),
                  }))
                }
                min={0}
                max={100}
                step={0.1}
                unit="%"
              />
              <SettingsBooleanField
                label="Comissão por produto"
                description="Permite definir taxas individuais por produto, sobrescrevendo a taxa padrão."
                checked={commissionForm.enableProductCommission}
                onCheckedChange={(v) =>
                  setCommissionForm((f) => ({
                    ...f,
                    enableProductCommission: v,
                  }))
                }
              />
            </div>
            <div className="mt-4 space-y-1.5">
              <Label className="text-sm font-medium">
                Observações de política
              </Label>
              <p className="text-xs text-muted-foreground">
                Anotações internas sobre a política de comissões da empresa.
              </p>
              <Textarea
                value={commissionForm.policyNotes}
                onChange={(e) =>
                  setCommissionForm((f) => ({
                    ...f,
                    policyNotes: e.target.value,
                  }))
                }
                placeholder="Ex: Comissão calculada sobre o valor líquido após descontos..."
                rows={3}
              />
            </div>
            <SaveFooter
              onSave={() => {
                if (commissionForm.defaultCommissionRate < 0) {
                  toast.error("Taxa de comissão não pode ser negativa.");
                  return;
                }
                save("comissoes", { commission: commissionForm });
              }}
              saving={isSaving("comissoes")}
            />
          </SectionCard>
        </TabsContent>

        {/* ── PDV ──────────────────────────────────────────────────────────── */}
        <TabsContent value="pdv" className="mt-6 space-y-4">
          <SectionCard title="Descontos">
            <div className="mb-2">
              <SettingsBooleanField
                label="Permitir desconto em valor (R$)"
                description="Operadores podem aplicar desconto como valor fixo na venda."
                checked={posForm.allowValueDiscount}
                onCheckedChange={(v) =>
                  setPosForm((f) => ({ ...f, allowValueDiscount: v }))
                }
              />
              <SettingsBooleanField
                label="Permitir desconto em porcentagem (%)"
                description="Operadores podem aplicar desconto percentual na venda."
                checked={posForm.allowPercentDiscount}
                onCheckedChange={(v) =>
                  setPosForm((f) => ({ ...f, allowPercentDiscount: v }))
                }
              />
              <SettingsNumberField
                label="Desconto máximo permitido"
                description="Desconto acima deste limite exige autorização gerencial."
                value={posForm.maxDiscountPercent}
                onChange={(v) =>
                  setPosForm((f) => ({
                    ...f,
                    maxDiscountPercent: Math.min(100, Math.max(0, v)),
                  }))
                }
                min={0}
                max={100}
                unit="%"
                inputWidth="w-20"
              />
            </div>
            <SaveFooter
              onSave={() => save("pdv-descontos", { pos: posForm })}
              saving={isSaving("pdv-descontos")}
            />
          </SectionCard>

          <SectionCard title="Segurança operacional">
            <div className="mb-2">
              <SettingsBooleanField
                label="Exigir autorização gerencial"
                description="Operações sensíveis (cancelamentos, descontos acima do limite) requerem senha de gerente ou diretoria."
                checked={posForm.requireManagerAuth}
                onCheckedChange={(v) =>
                  setPosForm((f) => ({ ...f, requireManagerAuth: v }))
                }
              />
            </div>
            <SaveFooter
              onSave={() => save("pdv-seguranca", { pos: posForm })}
              saving={isSaving("pdv-seguranca")}
            />
          </SectionCard>
        </TabsContent>

        {/* ── Sistema ──────────────────────────────────────────────────────── */}
        <TabsContent value="sistema" className="mt-6 space-y-4">
          <SectionCard title="Preferências gerais">
            <div className="space-y-4 mb-2">
              <SettingsField label="Idioma">
                <Select
                  value={systemForm.language}
                  onValueChange={(v) =>
                    setSystemForm((f) => ({ ...f, language: v }))
                  }
                >
                  <SelectTrigger className="w-52">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="pt-BR">Português (Brasil)</SelectItem>
                    <SelectItem value="en-US">English (US)</SelectItem>
                  </SelectContent>
                </Select>
              </SettingsField>

              <SettingsField label="Formato de data">
                <Select
                  value={systemForm.dateFormat}
                  onValueChange={(v) =>
                    setSystemForm((f) => ({ ...f, dateFormat: v }))
                  }
                >
                  <SelectTrigger className="w-52">
                    <SelectValue />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="dd/MM/yyyy">DD/MM/AAAA</SelectItem>
                    <SelectItem value="MM/dd/yyyy">MM/DD/AAAA</SelectItem>
                    <SelectItem value="yyyy-MM-dd">AAAA-MM-DD</SelectItem>
                  </SelectContent>
                </Select>
              </SettingsField>

              <SettingsField
                label="Símbolo de moeda"
                description="Exibido nos campos de valor em todo o sistema."
              >
                <Input
                  value={systemForm.currencySymbol}
                  onChange={(e) =>
                    setSystemForm((f) => ({
                      ...f,
                      currencySymbol: e.target.value,
                    }))
                  }
                  className="w-24"
                  maxLength={4}
                />
              </SettingsField>
            </div>
            <SaveFooter
              onSave={() => save("sistema", { system: systemForm })}
              saving={isSaving("sistema")}
            />
          </SectionCard>

          <SectionCard title="Integrações">
            <EmptyState
              icon={Plug}
              title="Integrações disponíveis em breve"
              description="Conectores com plataformas externas, APIs fiscais e serviços de pagamento serão configurados aqui."
            />
          </SectionCard>
        </TabsContent>
      </Tabs>
    </div>
  );
}
