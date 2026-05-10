import { defineConfig } from "vite";
import react from "@vitejs/plugin-react-swc";
import path from "path";

export default defineConfig(({ mode }) => ({
  server: {
    host: "::",
    port: 8080,
    hmr: { overlay: false },
    proxy: {
      // Proxy /api → backend, avoiding CORS in development
      "/api": {
        target: "https://localhost:61181",
        changeOrigin: true,
        secure: false,
        timeout: 600000,       // 10 min — well plan extractions take 3-8 min
        proxyTimeout: 600000,
      },
    },
  },
  plugins: [react()],
  resolve: {
    alias: {
      "@": path.resolve(__dirname, "./src"),
    },
  },
}));
