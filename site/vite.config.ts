import { defineConfig } from "vite";
import react from "@vitejs/plugin-react";
import tailwindcss from "@tailwindcss/vite";
import path from "path";

export default defineConfig({
  plugins: [react(), tailwindcss()],
  base: "/odd-snap/",
  resolve: {
    alias: {
      "@": path.resolve(__dirname, "./src"),
    },
  },
  build: {
    outDir: "../docs",
    emptyOutDir: false,
    rollupOptions: {
      output: {
        manualChunks(id) {
          if (!id.includes("node_modules")) return;
          if (id.includes("framer-motion")) return "motion";
          if (
            id.includes("lucide-react") ||
            id.includes("@tabler/icons") ||
            id.includes("@phosphor-icons") ||
            id.includes("@hugeicons")
          ) {
            return "icons";
          }
          if (id.includes("react") || id.includes("react-dom") || id.includes("react-router")) {
            return "react";
          }
          return "vendor";
        },
      },
    },
  },
});
