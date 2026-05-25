import { defineConfig } from "vite";
import react from "@vitejs/plugin-react-swc";
import path from "path";

export default defineConfig({
  server: {
    host: "::",
    port: 8080,
    hmr: {
      overlay: false,
    },
  },
  plugins: [react()],
  resolve: {
    alias: {
      "@": path.resolve(__dirname, "./src"),
    },
  },
  build: {
    chunkSizeWarningLimit: 600,
    rollupOptions: {
      output: {
        manualChunks(id) {
          // ── Vendor splits ─────────────────────────────────────────────────
          if (id.includes("node_modules")) {
            // SignalR: heavy, only loaded on restaurante/kitchen pages
            if (id.includes("@microsoft/signalr")) return "vendor-signalr";

            // Recharts + its D3 sub-deps: only loaded on chart pages
            if (id.includes("recharts") || id.includes("d3-") || id.includes("victory-"))
              return "vendor-charts";

            // React Query — shared, small, cache separately
            if (id.includes("@tanstack/react-query")) return "vendor-query";

            // Form stack — only loaded on form pages
            if (
              id.includes("react-hook-form") ||
              id.includes("@hookform") ||
              id.includes("/zod/")
            )
              return "vendor-forms";

            // Date utilities
            if (id.includes("date-fns")) return "vendor-dates";

            // Radix UI — many shadcn components use it
            if (id.includes("@radix-ui")) return "vendor-radix";

            // React + Router — smallest possible core, long-cached
            if (
              id.includes("/node_modules/react/") ||
              id.includes("/node_modules/react-dom/") ||
              id.includes("/node_modules/react-router") ||
              id.includes("/node_modules/scheduler/")
            )
              return "vendor-react";

            // lucide-react: no separate chunk — let Rollup co-locate icons
            // with the page chunks that use them. Shell icons inline into
            // index.js (~10KB), page icons go into their lazy chunks.

            // Everything else: no explicit chunk. Rollup will:
            // - inline into index.js if used by static imports
            // - bundle into the lazy page chunk if only used there
            // - create a shared chunk if used by 2+ lazy chunks
          }

          // ── App module splits ─────────────────────────────────────────────
          // Group pages + hooks + components by module so navigating within
          // a section needs only one chunk download instead of one per page.

          if (id.includes("/src/modules/restaurante/")) return "app-restaurante";
          if (id.includes("/src/modules/platform/"))   return "app-platform";
          if (id.includes("/src/modules/portal/"))     return "app-portal";
          if (id.includes("/src/modules/landing/"))    return "app-landing";
          if (id.includes("/src/modules/dashboard/"))  return "app-dashboard";
          if (id.includes("/src/modules/reports/"))    return "app-reports";

          // All other app files (sales, products, customers, layouts, shared
          // components, utilities) use Rollup's default splitting — each lazy
          // page gets its own chunk, shared helpers go into a common chunk.
        },
      },
    },
  },
});
