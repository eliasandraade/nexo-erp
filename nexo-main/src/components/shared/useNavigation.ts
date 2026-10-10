import { appRoutes, type RouteGroup } from "@/app/router/routes";
import { useAuth } from "@/modules/auth/context/AuthContext";
import { useWorkspace } from "@/modules/workspace/WorkspaceContext";
import { useHasServiceModule } from "@/modules/service/hooks/useHasServiceModule";
import { useServicePresetOptional } from "@/modules/service/context/ServicePresetContext";
const VERTICAL_GROUPS: RouteGroup[] = ["varejo", "restaurante", "build", "service"];
/** Discoverability only; route guards remain authoritative. */
export function useNavigation() {
  const { session } = useAuth();
  const { active } = useWorkspace();
  const hasService = useHasServiceModule();
  const servicePreset = useServicePresetOptional();
  const visibleRoutes = appRoutes.filter((route) => {
    if (route.moduleKey && !session?.modules.includes(route.moduleKey)) return false;
    // Service is a module family (decision D1): any vertical key unlocks the group, so it
    // can't use a single `moduleKey`. Capability-gated surfaces (decision D2) appear only
    // when the resolved preset enables them — preset is null outside the Service area.
    if (route.group === "service") {
      if (!hasService) return false;
      // Public booking keeps capability-gated surfaces (the Agenda) discoverable for verticals
      // without that capability, since the portal creates appointments to manage.
      const bookingOverride = !!route.showWhenPublicBooking && !!servicePreset?.publicBookingEnabled;
      if (route.capability && !servicePreset?.capabilities?.[route.capability] && !bookingOverride)
        return false;
      if (route.capabilityAny && !route.capabilityAny.some((c) => servicePreset?.capabilities?.[c]) && !bookingOverride)
        return false;
    }
    if (route.roles && session?.role && !route.roles.includes(session.role)) return false;
    // Show one operation at a time: a vertical group only appears in its own
    // workspace. Shared groups (core, inventário, admin) always pass through.
    if (active && VERTICAL_GROUPS.includes(route.group) && route.group !== active.group) {
      return false;
    }
    return true;
  });

  return visibleRoutes;
}
