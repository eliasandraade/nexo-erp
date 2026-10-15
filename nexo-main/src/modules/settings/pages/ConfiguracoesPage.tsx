import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { Save, ScrollText } from "lucide-react";
import { toast } from "sonner";
import { PageHeader } from "@/components/shared/PageHeader";
import { SectionCard } from "@/components/shared/SectionCard";
import { ErrorState } from "@/components/shared/ErrorState";
import { Button } from "@/components/ui/button";
import { Skeleton } from "@/components/ui/skeleton";
import { SettingsBooleanField } from "../components/SettingsBooleanField";
import { SettingsNumberField } from "../components/SettingsNumberField";
import { SETTINGS_KEY, saveSettings, useSettings } from "../api/settings.api";
import type { AppSettings } from "../types";
import { settingsProblems } from "../lib/rules";

/**
 * Configurações da empresa — só o que muda alguma coisa: os alertas de estoque do painel e as
 * regras de desconto do PDV (o limite e a autorização de gerente valem também no servidor).
 * Dados da empresa para nota e recibo ficam em Fiscal.
 */
export default function ConfiguracoesPage() {
  const qc = useQueryClient();
  const settings = useSettings();
  const [form, setForm] = useState<AppSettings | null>(null);

  useEffect(() => {
    if (settings.data) setForm(settings.data);
  }, [settings.data]);

  const save = useMutation({
    mutationFn: (value: AppSettings) => saveSettings(value),
    onSuccess: (saved) => {
      qc.setQueryData(SETTINGS_KEY, saved);
      toast.success("Configurações salvas.");
    },
    onError: (e: Error) => toast.error(e.message),
  });

  if (settings.isLoading || (!form && !settings.isError)) return <Skeleton className="h-96 w-full" />;
  if (settings.isError || !form) return <ErrorState title="Não foi possível carregar as configurações" onRetry={() => void settings.refetch()} />;

  const problems = settingsProblems(form);
  const setInventory = (patch: Partial<AppSettings["inventory"]>) => setForm({ ...form, inventory: { ...form.inventory, ...patch } });
  const setPos = (patch: Partial<AppSettings["pos"]>) => setForm({ ...form, pos: { ...form.pos, ...patch } });

  return (
    <div className="space-y-6">
      <PageHeader
        title="Configurações"
        description="Regras que valem para a empresa toda."
        actions={
          <Button onClick={() => save.mutate(form)} disabled={save.isPending || problems.length > 0}>
            <Save className="mr-2 h-4 w-4" /> {save.isPending ? "Salvando…" : "Salvar"}
          </Button>
        }
      />

      <SectionCard title="PDV — descontos" description="Valem no caixa. O limite e a autorização de gerente também são conferidos pelo servidor ao finalizar a venda.">
        <SettingsBooleanField
          label="Desconto em valor (R$)"
          description="O operador pode digitar o desconto em reais."
          checked={form.pos.allowValueDiscount}
          onCheckedChange={(v) => setPos({ allowValueDiscount: v })}
        />
        <SettingsBooleanField
          label="Desconto em porcentagem (%)"
          description="O operador pode digitar o desconto em porcentagem."
          checked={form.pos.allowPercentDiscount}
          onCheckedChange={(v) => setPos({ allowPercentDiscount: v })}
        />
        <SettingsNumberField
          label="Desconto máximo sem gerente"
          description="Somando os descontos dos itens e o da venda, sobre o valor bruto. Gerentes e diretores não têm limite."
          value={form.pos.maxDiscountPercent}
          onChange={(v) => setPos({ maxDiscountPercent: v })}
          min={0}
          max={100}
          step={1}
          unit="%"
        />
        <SettingsBooleanField
          label="Gerente pode autorizar no caixa"
          description="Acima do limite, um gerente digita login e senha no PDV para liberar. Desligado, o desconto acima do limite só sai com um gerente operando o caixa."
          checked={form.pos.requireManagerAuth}
          onCheckedChange={(v) => setPos({ requireManagerAuth: v })}
        />
      </SectionCard>

      <SectionCard title="Estoque — alertas no painel" description="O que a Visão geral avisa sobre o estoque.">
        <SettingsBooleanField
          label="Produtos sem estoque"
          description="Avisa quando nada está disponível para vender."
          checked={form.inventory.enableZeroStockAlerts}
          onCheckedChange={(v) => setInventory({ enableZeroStockAlerts: v })}
        />
        <SettingsBooleanField
          label="Produtos abaixo do mínimo"
          description="Avisa quando o disponível fica abaixo do estoque mínimo do produto."
          checked={form.inventory.enableLowStockAlerts}
          onCheckedChange={(v) => setInventory({ enableLowStockAlerts: v })}
        />
      </SectionCard>

      {problems.length > 0 && (
        <ul className="list-disc pl-5 text-sm text-destructive">{problems.map((p) => <li key={p}>{p}</li>)}</ul>
      )}

      <SectionCard title="Dados da empresa">
        <p className="flex flex-wrap items-center gap-2 text-sm text-muted-foreground">
          <ScrollText className="h-4 w-4" aria-hidden="true" />
          Razão social, CNPJ, inscrições e endereço — usados no recibo e, no futuro, na nota fiscal — ficam em
          <Link to="/fiscal" className="font-medium text-primary hover:underline">Fiscal</Link>.
        </p>
      </SectionCard>
    </div>
  );
}
