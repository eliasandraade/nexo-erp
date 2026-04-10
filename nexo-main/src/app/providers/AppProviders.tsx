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
      // 30-second freshness window prevents redundant refetches while
      // navigating between pages. Mutations that change data call
      // queryClient.invalidateQueries() explicitly to force immediate refresh.
      staleTime: 30_000,
      retry: 1,
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
