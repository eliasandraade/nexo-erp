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
    // Increase warning threshold to avoid noise (default 500kb)
    chunkSizeWarningLimit: 600,
    rollupOptions: {
      output: {
        manualChunks: {
          // React core — changes rarely, caches long
          "vendor-react": ["react", "react-dom", "react-router-dom"],
          // SignalR — large library, only needed in restaurante pages
          "vendor-signalr": ["@microsoft/signalr"],
          // Radix UI + shadcn — large, changes rarely
          "vendor-radix": [
            "@radix-ui/react-dialog",
            "@radix-ui/react-dropdown-menu",
            "@radix-ui/react-select",
            "@radix-ui/react-tabs",
            "@radix-ui/react-tooltip",
            "@radix-ui/react-popover",
            "@radix-ui/react-label",
            "@radix-ui/react-separator",
            "@radix-ui/react-switch",
            "@radix-ui/react-checkbox",
            "@radix-ui/react-avatar",
            "@radix-ui/react-slot",
          ],
          // Charts — Recharts is heavy (~300kb)
          "vendor-charts": ["recharts"],
          // React Query — data fetching layer
          "vendor-query": ["@tanstack/react-query"],
          // Form libraries
          "vendor-forms": ["react-hook-form", "@hookform/resolvers", "zod"],
          // Date utilities
          "vendor-dates": ["date-fns"],
          // Lucide icons — large if imported naively
          "vendor-icons": ["lucide-react"],
        },
      },
    },
  },
});
