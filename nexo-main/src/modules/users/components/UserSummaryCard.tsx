import { SectionCard } from "@/components/shared/SectionCard";
import { StatusBadge } from "@/components/shared/StatusBadge";
import { format } from "date-fns";
import type { User } from "../types";
import { roleLabels, statusLabels, statusVariant } from "../types";

interface Props {
  user: User;
}

export function UserSummaryCard({ user }: Props) {
  return (
    <SectionCard title="Resumo do usuário">
      <dl className="grid grid-cols-2 gap-x-4 gap-y-2 text-sm">
        <dt className="text-muted-foreground">Perfil</dt>
        <dd className="font-medium">{roleLabels[user.role]}</dd>
        <dt className="text-muted-foreground">Loja</dt>
        <dd>{user.store || "—"}</dd>
        <dt className="text-muted-foreground">Status</dt>
        <dd><StatusBadge label={statusLabels[user.status]} variant={statusVariant[user.status]} /></dd>
        <dt className="text-muted-foreground">Criado por</dt>
        <dd>{user.createdBy}</dd>
        <dt className="text-muted-foreground">Criado em</dt>
        <dd>{format(new Date(user.createdAt), "dd/MM/yyyy")}</dd>
        <dt className="text-muted-foreground">Última atualização</dt>
        <dd>{format(new Date(user.updatedAt), "dd/MM/yyyy")}</dd>
        <dt className="text-muted-foreground">Último acesso</dt>
        <dd>{user.lastAccess ? format(new Date(user.lastAccess), "dd/MM/yyyy HH:mm") : "—"}</dd>
        <dt className="text-muted-foreground">Última troca de senha</dt>
        <dd>{user.lastPasswordChange ? format(new Date(user.lastPasswordChange), "dd/MM/yyyy") : "—"}</dd>
      </dl>
    </SectionCard>
  );
}
