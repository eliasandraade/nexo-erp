import { useEffect } from "react";
import { sessionAppUrl } from "@/services/api-client";

/**
 * In the cookie mode only the app of the ERP has authority over the session, and this bundle also serves the public portals on another host. A page that needs a
 * session leaves for the app with its whole address, and draws nothing meanwhile. True while leaving. In the default build, and on the host of the app, it does nothing.
 */
export function useLeaveForSessionApp(): boolean {
  const target = sessionAppUrl();
  useEffect(() => {
    if (target) window.location.replace(target);
  }, [target]);
  return target !== null;
}
