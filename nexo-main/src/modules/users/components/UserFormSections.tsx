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
        <Label>Nome completo *</Label>
        <Input value={form.name} onChange={(e) => onChange({ name: e.target.value })} className={errors.name ? "border-destructive" : ""} />
        {errors.name && <p className="text-xs text-destructive">{errors.name}</p>}
      </div>
      <div className="space-y-1.5">
        <Label>E-mail *</Label>
        <Input type="email" value={form.email} onChange={(e) => onChange({ email: e.target.value })} className={errors.email ? "border-destructive" : ""} />
        {errors.email && <p className="text-xs text-destructive">{errors.email}</p>}
      </div>
      <div className="space-y-1.5">
        <Label>Login *</Label>
        <Input value={form.login} onChange={(e) => onChange({ login: e.target.value })} className={errors.login ? "border-destructive" : ""} />
        {errors.login && <p className="text-xs text-destructive">{errors.login}</p>}
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

export function UserAccessSection({ form, onChange, errors, stores }: Props & { stores: string[] }) {
  const requiresStore = form.role === "gerente" || form.role === "vendedor";

  return (
    <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
      <div className="space-y-1.5">
        <Label>Perfil *</Label>
        <Select value={form.role} onValueChange={(v) => onChange({ role: v as UserRole, store: "" })}>
          <SelectTrigger className={errors.role ? "border-destructive" : ""}><SelectValue /></SelectTrigger>
          <SelectContent>
            {(Object.keys(roleLabels) as UserRole[]).map((r) => (
              <SelectItem key={r} value={r}>{roleLabels[r]}</SelectItem>
            ))}
          </SelectContent>
        </Select>
        {errors.role && <p className="text-xs text-destructive">{errors.role}</p>}
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
  return (
    <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
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
      <div className="flex items-center gap-3 pt-2">
        <Switch checked={form.blocked} onCheckedChange={(v) => onChange({ blocked: v })} />
        <Label className="cursor-pointer">Bloqueado</Label>
      </div>
    </div>
  );
}
