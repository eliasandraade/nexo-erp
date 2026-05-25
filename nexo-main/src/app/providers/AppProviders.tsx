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
      staleTime: 30_000,
      retry: 1,
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
