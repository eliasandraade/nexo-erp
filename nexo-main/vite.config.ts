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
            // clsx + tailwind-merge back cn() (used by nearly every component,
            // including eager layouts) AND are pulled in by recharts. Pin them to
            // the base react chunk so they're a leaf everything depends ON — never
            // a cross-edge. Otherwise Rollup parks clsx inside vendor-charts and
            // vendor-react imports it back, forming a vendor-react <-> vendor-charts
            // cycle that left React bindings in the temporal dead zone and
            // white-screened cold loads ("Cannot access 'P'/'Q' before init").
            if (
              id.includes("/node_modules/clsx/") ||
              id.includes("/node_modules/tailwind-merge/")
            )
              return "vendor-react";

            // SignalR: heavy, only loaded on restaurante/kitchen pages
            if (id.includes("@microsoft/signalr")) return "vendor-signalr";

            // NOTE: recharts/d3/victory are intentionally NOT pinned to a manual
            // chunk. recharts imports React, and a binding shared between the React
            // ecosystem and recharts ended up in that manual "vendor-charts" chunk,
            // which vendor-react then imported back — a vendor-react <-> vendor-charts
            // cycle that put React in the temporal dead zone and white-screened cold
            // loads (e.g. the public portal). Letting Rollup auto-chunk recharts keeps
            // it acyclic; it still loads lazily via SalesChart's dynamic import().

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

          // ── App code: let Rollup chunk it automatically ───────────────────
          // We intentionally do NOT manually group app code by feature folder.
          // Grouping (app-dashboard, app-portal, app-restaurante, …) scattered
          // shared leaf modules (UI primitives, cn, hooks) across those feature
          // chunks, so each feature chunk imported the others to reach them — a
          // CIRCULAR chunk graph. On a cold load of the public portal (/:slug),
          // its circular partners weren't initialized yet, leaving React /
          // React-Query bindings in the temporal dead zone → white screen
          // ("Cannot access 'Q' before initialization" / createContext on
          // undefined). Rollup's automatic per-dynamic-import chunking is acyclic
          // by design and still code-splits every lazy route. SalesChart keeps
          // its own lazy boundary via the dynamic import() in DashboardPage, so
          // recharts (vendor-charts) still streams in after the KPIs.
        },
      },
    },
  },
});
