import { CheckCircle2, XCircle, Clock } from "lucide-react";
import type { CashSessionDto } from "../types";
import { formatCurrency, formatDateTime } from "@/lib/formatters";

interface CashStatusBannerProps {
  session: CashSessionDto | null;
  expectedBalance: number;
}

export function CashStatusBanner({ session, expectedBalance }: CashStatusBannerProps) {
  if (!session || session.status !== "Open") {
    return (
      <div className="flex items-center gap-3 rounded-xl border border-border bg-muted/40 px-5 py-4">
        <XCircle className="h-5 w-5 text-muted-foreground shrink-0" />
        <div>
          <p className="text-sm font-semibold text-foreground">Caixa fechado</p>
          <p className="text-xs text-muted-foreground mt-0.5">
            Nenhuma sessão em andamento. Abra o caixa para registrar movimentações.
          </p>
        </div>
      </div>
    );
  }

  return (
    <div className="flex flex-col sm:flex-row sm:items-center sm:justify-between gap-4 rounded-lg border border-green-200 bg-green-50 dark:border-green-900 dark:bg-green-950/30 px-5 py-4">
      <div className="flex items-center gap-3">
        <CheckCircle2 className="h-5 w-5 text-green-600 dark:text-green-400 shrink-0" />
        <div>
          <p className="text-sm font-semibold text-green-800 dark:text-green-300">
            Caixa aberto
          </p>
          <p className="text-xs text-green-700 dark:text-green-400 mt-0.5">
            Operador: <span className="font-medium">{session.openedByName}</span>
          </p>
        </div>
      </div>
      <div className="flex items-center gap-6 text-sm">
        <div className="flex items-center gap-1.5 text-green-700 dark:text-green-400">
          <Clock className="h-3.5 w-3.5" />
          <span className="text-xs">{formatDateTime(session.openedAt)}</span>
        </div>
        <div className="text-right">
          <p className="text-xs text-green-600 dark:text-green-500">Saldo esperado</p>
          <p className="text-base font-bold text-green-800 dark:text-green-200">
            {formatCurrency(expectedBalance)}
          </p>
        </div>
      </div>
    </div>
  );
}
