import { useEffect, useState } from "react";
import { Loader2, Mail } from "lucide-react";
import { toast } from "sonner";
import { Button } from "@/components/ui/button";
import {
  Select, SelectContent, SelectItem, SelectTrigger, SelectValue,
} from "@/components/ui/select";
import { cn } from "@/lib/utils";
import { ApiError } from "@/services/api-client";
import { useUpdateCustomerEmails } from "../hooks/usePublicBookingSettings";
import type { PublicBookingSettingsDto } from "../api/service.api";

const LEADS = [2, 6, 12, 24, 48];

/** Confirmation + reminder e-mails to the customer, each with their own link to view or cancel. */
export function PortalCustomerEmailsForm({ settings }: { settings: PublicBookingSettingsDto }) {
  const mut = useUpdateCustomerEmails();
  const [enabled, setEnabled] = useState(false);
  const [hours, setHours] = useState(24);

  // re-sync only when these values change — another section saving must not wipe an unsaved edit
  const savedEnabled = settings.customerEmailsEnabled ?? false;
  const savedHours = settings.reminderHoursBefore ?? 24;
  useEffect(() => { setEnabled(savedEnabled); setHours(savedHours); }, [savedEnabled, savedHours]);

  const save = () => mut.mutate(
    { customerEmailsEnabled: enabled, reminderHoursBefore: hours },
    {
      onSuccess: () => toast.success(enabled ? "Avisos por e-mail ligados." : "Avisos por e-mail desligados."),
      onError: (e) => toast.error(e instanceof ApiError ? e.message : "Não foi possível salvar os avisos."),
    },
  );

  return (
    <div className="space-y-4">
      <button
        type="button"
        role="switch"
        aria-checked={enabled}
        onClick={() => setEnabled(!enabled)}
        className={cn(
          "flex w-full items-center gap-4 rounded-md border p-4 text-left transition-colors",
          enabled ? "border-primary/40 bg-primary/5" : "border-border bg-card hover:border-border/80",
        )}
      >
        <div className={cn("shrink-0 rounded-lg p-2", enabled ? "bg-primary/10 text-primary" : "bg-muted text-muted-foreground")}>
          <Mail className="h-4 w-4" />
        </div>
        <div className="min-w-0 flex-1">
          <p className="text-sm font-medium">Avisar clientes por e-mail</p>
          <p className="mt-0.5 text-xs text-muted-foreground">
            Confirmação de quem agenda pelo portal e lembrete antes do horário, com o link para ver ou cancelar.
            Só vale para clientes com e-mail cadastrado.
          </p>
        </div>
        <div className={cn("relative h-5 w-9 shrink-0 rounded-full transition-colors", enabled ? "bg-primary" : "bg-muted")}>
          <div className={cn("absolute top-0.5 h-4 w-4 rounded-full bg-white shadow transition-transform", enabled ? "translate-x-4" : "translate-x-0.5")} />
        </div>
      </button>

      <div className="space-y-1.5">
        <label htmlFor="reminder-hours" className="text-xs font-medium uppercase tracking-wide text-muted-foreground">
          Lembrete
        </label>
        <Select value={String(hours)} onValueChange={(v) => setHours(Number(v))} disabled={!enabled}>
          <SelectTrigger id="reminder-hours" className="h-9 w-56"><SelectValue /></SelectTrigger>
          <SelectContent>
            {[...new Set([...LEADS, hours])].sort((a, b) => a - b).map((h) => (
              <SelectItem key={h} value={String(h)}>{h} horas antes</SelectItem>
            ))}
          </SelectContent>
        </Select>
      </div>

      {settings.emailDeliveryAvailable === false && (
        <p role="note" className="text-xs text-amber-600">
          O envio de e-mails não está configurado neste ambiente: os avisos ficam aguardando até ele ser ativado.
        </p>
      )}

      <Button onClick={save} disabled={mut.isPending}>
        {mut.isPending ? <><Loader2 className="mr-2 h-4 w-4 animate-spin" />Salvando...</> : "Salvar avisos"}
      </Button>
    </div>
  );
}
