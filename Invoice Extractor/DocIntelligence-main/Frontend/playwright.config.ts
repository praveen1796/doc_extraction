import { defineConfig, devices } from "@playwright/test";
import path from "path";
import { fileURLToPath } from "url";

const frontendRoot = path.dirname(fileURLToPath(import.meta.url));

export default defineConfig({
  testDir: "./e2e",
  fullyParallel: false,
  forbidOnly: !!process.env.CI,
  retries: 0,
  workers: 1,
  reporter: "list",
  timeout: 180_000,
  use: {
    baseURL: "http://127.0.0.1:5174",
    trace: "retain-on-failure",
    video: "off",
  },
  projects: [{ name: "chromium", use: { ...devices["Desktop Chrome"] } }],
  webServer: {
    // build + preview avoids dev-server resolving H: → OneDrive symlink to a broken tree
    command: "npm run build && npx vite preview --host 127.0.0.1 --port 5174 --strictPort",
    cwd: frontendRoot,
    url: "http://127.0.0.1:5174",
    reuseExistingServer: false,
    timeout: 300_000,
  },
});
