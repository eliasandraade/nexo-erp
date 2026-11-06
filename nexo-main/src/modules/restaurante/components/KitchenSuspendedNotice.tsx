import { ShieldAlert } from "lucide-react";

interface KitchenSuspendedNoticeProps {
  /** The screen looks for the access by itself; otherwise the person asks for the check. */
  checksByItself: boolean;
  checking: boolean;
  onCheckAgain: () => void;
}

/** Why nothing else is reaching this screen, for whoever is working in the kitchen. The session stays: nothing here signs anyone out. */
export function KitchenSuspendedNotice({ checksByItself, checking, onCheckAgain }: KitchenSuspendedNoticeProps) {
  return (
    <div role="alert" className="mb-4 flex flex-wrap items-center gap-3 rounded-md border border-amber-500/50 bg-amber-950/40 px-4 py-3">
      <ShieldAlert aria-hidden="true" className="h-5 w-5 shrink-0 text-amber-400" />
      <div className="min-w-0 flex-1 text-sm">
        <p className="font-semibold text-amber-200">O acesso da empresa ao sistema está suspenso.</p>
        <p className="text-amber-100/90">
          Novos pedidos não estão chegando nesta tela, e os que aparecem aqui podem estar desatualizados.
          Confirme os pedidos com o salão e avise o responsável pela empresa.
          {checksByItself
            ? " A tela volta ao normal sozinha quando o acesso for restabelecido."
            : " Quando o acesso voltar, toque em Verificar de novo."}
        </p>
      </div>
      {!checksByItself && (
        <button
          type="button"
          onClick={onCheckAgain}
          disabled={checking}
          className="rounded-lg bg-amber-700/60 px-3 py-2 text-sm font-medium transition-colors hover:bg-amber-700/80 disabled:opacity-50"
        >
          {checking ? "Verificando..." : "Verificar de novo"}
        </button>
      )}
    </div>
  );
}
