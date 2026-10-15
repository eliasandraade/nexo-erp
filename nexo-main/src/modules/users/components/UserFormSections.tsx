import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import {
  Select, SelectContent, SelectItem, SelectTrigger, SelectValue,
} from "@/components/ui/select";
import { Switch } from "@/components/ui/switch";
import type { UserFormInput, UserRole, UserStatus } from "../types";
import { roleLabels, statusLabels } from "../types";

interface Props {
  form: UserFormInput;
  onChange: (patch: Partial<UserFormInput>) => void;
  errors: Record<string, string>;
}

export function UserMainDataSection({ form, onChange, errors }: Props) {
  return (
    <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
      <div className="space-y-1.5">
        <Label htmlFor="user-name">Nome completo *</Label>
        <Input id="user-name" value={form.name} onChange={(e) => onChange({ name: e.target.value })} className={errors.name ? "border-destructive" : ""} />
        {errors.name && <p className="text-xs text-destructive">{errors.name}</p>}
      </div>
      <div className="space-y-1.5">
        <Label htmlFor="user-email">E-mail *</Label>
        <Input id="user-email" type="email" value={form.email} onChange={(e) => onChange({ email: e.target.value })} className={errors.email ? "border-destructive" : ""} />
        {errors.email && <p className="text-xs text-destructive">{errors.email}</p>}
      </div>
      <div className="space-y-1.5">
        <Label htmlFor="user-login">Login *</Label>
        <Input id="user-login" value={form.login} onChange={(e) => onChange({ login: e.target.value })} className={errors.login ? "border-destructive" : ""} />
        {errors.login
          ? <p className="text-xs text-destructive">{errors.login}</p>
          : <p className="text-xs text-muted-foreground">Apenas letras, números, ponto, _ e - (sem espaços)</p>
        }
      </div>
      <div className="space-y-1.5">
        <Label>Telefone</Label>
        <Input value={form.phone} onChange={(e) => onChange({ phone: e.target.value })} />
      </div>
      <div className="space-y-1.5">
        <Label>Status</Label>
        <Select value={form.status} onValueChange={(v) => onChange({ status: v as UserStatus })}>
          <SelectTrigger><SelectValue /></SelectTrigger>
          <SelectContent>
            {(["active", "inactive"] as UserStatus[]).map((s) => (
              <SelectItem key={s} value={s}>{statusLabels[s]}</SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>
    </div>
  );
}

const roleHints: Partial<Record<UserRole, string>> = {
  diretoria:  "Acesso total ao sistema",
  gerente:    "Controle completo da loja vinculada",
  vendedor:   "PDV (varejo) ou piso/delivery (restaurante)",
  estoquista: "Acessa apenas Produtos e Estoque",
  cozinha:    "Acessa apenas a visão de Cozinha (módulo restaurante)",
};

export function UserAccessSection({ form, onChange, errors, stores }: Props & { stores: string[] }) {
  const requiresStore = form.role === "gerente" || form.role === "vendedor";

  return (
    <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
      <div className="space-y-1.5">
        <Label>Perfil *</Label>
        <Select value={form.role} onValueChange={(v) => onChange({ role: v as UserRole, store: "" })}>
          <SelectTrigger aria-label="Perfil" className={errors.role ? "border-destructive" : ""}><SelectValue /></SelectTrigger>
          <SelectContent>
            {(Object.keys(roleLabels) as UserRole[]).map((r) => (
              <SelectItem key={r} value={r}>{roleLabels[r]}</SelectItem>
            ))}
          </SelectContent>
        </Select>
        {errors.role
          ? <p className="text-xs text-destructive">{errors.role}</p>
          : form.role && roleHints[form.role] && (
              <p className="text-xs text-muted-foreground">{roleHints[form.role]}</p>
            )
        }
      </div>
      <div className="space-y-1.5">
        <Label>Empresa</Label>
        <Input value={form.company} onChange={(e) => onChange({ company: e.target.value })} />
      </div>
      <div className="space-y-1.5">
        <Label>Loja vinculada {requiresStore ? "*" : ""}</Label>
        <Select value={form.store} onValueChange={(v) => onChange({ store: v })}>
          <SelectTrigger className={errors.store ? "border-destructive" : ""}><SelectValue placeholder="Selecione" /></SelectTrigger>
          <SelectContent>
            <SelectItem value="none">Nenhuma</SelectItem>
            {stores.map((s) => (
              <SelectItem key={s} value={s}>{s}</SelectItem>
            ))}
          </SelectContent>
        </Select>
        {errors.store && <p className="text-xs text-destructive">{errors.store}</p>}
      </div>
      <div className="space-y-1.5 md:col-span-2">
        <Label>Observações administrativas</Label>
        <Input value={form.notes} onChange={(e) => onChange({ notes: e.target.value })} placeholder="Notas internas sobre este usuário" />
      </div>
    </div>
  );
}

export function UserSecuritySection({ form, onChange, errors, isEdit }: Props & { isEdit: boolean }) {
  const inviting = !isEdit && !!form.sendInvite;
  return (
    <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
      {!isEdit && (
        <div className="flex items-start gap-3 md:col-span-2">
          <Switch id="send-invite" checked={!!form.sendInvite} onCheckedChange={(v) => onChange({ sendInvite: v })} />
          <div>
            <Label htmlFor="send-invite" className="cursor-pointer">Enviar convite por e-mail</Label>
            <p className="text-xs text-muted-foreground">A pessoa recebe um link para criar a própria senha (vale 72 horas). Ninguém mais fica sabendo dela.</p>
          </div>
        </div>
      )}
      {!inviting && (<>
      <div className="space-y-1.5">
        <Label>{isEdit ? "Nova senha" : "Senha *"}</Label>
        <Input type="password" value={form.password ?? ""} onChange={(e) => onChange({ password: e.target.value })} className={errors.password ? "border-destructive" : ""} />
        {errors.password && <p className="text-xs text-destructive">{errors.password}</p>}
      </div>
      <div className="space-y-1.5">
        <Label>Confirmar senha</Label>
        <Input type="password" value={form.passwordConfirm ?? ""} onChange={(e) => onChange({ passwordConfirm: e.target.value })} className={errors.passwordConfirm ? "border-destructive" : ""} />
        {errors.passwordConfirm && <p className="text-xs text-destructive">{errors.passwordConfirm}</p>}
      </div>
      <div className="flex items-center gap-3 pt-2">
        <Switch checked={form.requirePasswordChange} onCheckedChange={(v) => onChange({ requirePasswordChange: v })} />
        <Label className="cursor-pointer">Exigir troca de senha no próximo acesso</Label>
      </div>
      </>)}
      <div className="flex items-center gap-3 pt-2">
        <Switch checked={form.blocked} onCheckedChange={(v) => onChange({ blocked: v })} />
        <Label className="cursor-pointer">Bloqueado</Label>
      </div>
    </div>
  );
}
