import { useCallback, useRef, useState, type MouseEvent } from "react";
import type { PortalActions } from "./api/gateway";

// Signed URLs (~15 min) are never kept: when one fails or is about to expire, the portal asks the API again. One renewal
// per minute at most, so a file that stays unavailable never turns into a request loop.
const RENEW_INTERVAL = 60000;

export interface SignedMedia {
  /** For <img>/<video> onError: renews the signed URLs once, transparently. */
  onError: () => void;
  /** For links to signed files: an expired link is renewed instead of opened. */
  guardLink: (e: MouseEvent<HTMLAnchorElement>) => void;
  notice: string;
}

export function useSignedMedia(
  actions: PortalActions,
  refreshAt?: string,
): SignedMedia {
  const last = useRef(0);
  const [notice, setNotice] = useState("");
  const renew = useCallback(() => {
    if (!actions.refresh || Date.now() - last.current < RENEW_INTERVAL)
      return false;
    last.current = Date.now();
    void actions.refresh();
    return true;
  }, [actions]);
  const onError = useCallback(() => {
    renew();
  }, [renew]);
  const guardLink = useCallback(
    (e: MouseEvent<HTMLAnchorElement>) => {
      if (!refreshAt || Date.now() < Date.parse(refreshAt)) return;
      e.preventDefault();
      setNotice(
        renew()
          ? "O link do arquivo foi renovado. Toque novamente para abrir."
          : "O arquivo está sendo atualizado. Tente novamente em instantes.",
      );
    },
    [refreshAt, renew],
  );
  return { onError, guardLink, notice };
}
