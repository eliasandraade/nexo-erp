import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { TooltipProvider } from "@/components/ui/tooltip";
import { Toaster } from "@/components/ui/sonner";

/**
 * All pages use `toast` from "sonner" — the shadcn/ui <Toaster> (Radix-based)
 * was an unused duplicate. Removed to avoid two competing toast stacks.
 */
const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      // Default freshness; per-hook staleTime overrides this (auth/tenant longer,
      // dashboard/sales shorter). 30s avoids refetching on every remount.
      staleTime: 30_000,
      // Keep inactive cache for 5min so navigating back to a page is instant
      // (served from cache) instead of refetching.
      gcTime: 5 * 60_000,
      // Don't retry client errors (401/403/404/422) — they won't succeed on
      // retry and just add latency. Retry transient server/network errors once.
      retry: (failureCount, error) => {
        const status = (error as { status?: number })?.status;
        if (status && status >= 400 && status < 500) return false;
        return failureCount < 1;
      },
      // Disabling window-focus refetch prevents 4-6 simultaneous requests
      // every time the user alt-tabs back to the app. Mutations still call
      // invalidateQueries() explicitly when data actually changes.
      refetchOnWindowFocus: false,
    },
  },
});

interface AppProvidersProps {
  children: React.ReactNode;
}

export function AppProviders({ children }: AppProvidersProps) {
  return (
    <QueryClientProvider client={queryClient}>
      <TooltipProvider>
        <Toaster />
        {children}
      </TooltipProvider>
    </QueryClientProvider>
  );
}
