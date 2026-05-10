import { test, expect } from "@playwright/test";
import * as fs from "fs";
import * as path from "path";
import { fileURLToPath } from "url";

const __dirname = path.dirname(fileURLToPath(import.meta.url));
const smartPlanDir = path.join(__dirname, "..", "SmartPlanDocuments");

function pickSmallestPdf(): string | null {
  if (!fs.existsSync(smartPlanDir)) return null;
  const files = fs
    .readdirSync(smartPlanDir)
    .filter((f) => f.toLowerCase().endsWith(".pdf"))
    .map((f) => ({ f, s: fs.statSync(path.join(smartPlanDir, f)).size }))
    .sort((a, b) => a.s - b.s);
  if (!files.length) return null;
  return path.join(smartPlanDir, files[0].f);
}

test.describe("Well plan → workspace + JSON download", () => {
  test.beforeEach(async ({ page }) => {
    await page.addInitScript(() => {
      localStorage.setItem("dociq_demo_mode", "true");
    });
  });

  test("upload, see structured data, download and validate JSON", async ({ page }) => {
    const pdfPath = pickSmallestPdf();
    test.skip(!pdfPath, "No PDF in Frontend/SmartPlanDocuments");

    await page.goto("/upload");

    await page.getByRole("button", { name: /Well Plan.*Drilling Program/i }).click();

    const fileInput = page.locator('input[type="file"]');
    await fileInput.setInputFiles(pdfPath);

    await page.waitForURL("**/workspace", { timeout: 60_000 });

    await expect(page.getByText("Nabors X12")).toBeVisible({ timeout: 30_000 });

    const downloadBtn = page.getByTitle("Download extraction JSON");
    await expect(downloadBtn).toBeVisible({ timeout: 30_000 });
    await expect(downloadBtn).toBeEnabled();

    const [download] = await Promise.all([
      page.waitForEvent("download"),
      downloadBtn.click(),
    ]);

    const name = download.suggestedFilename();
    expect(name).toMatch(/\.json$/i);

    const tmp = path.join(smartPlanDir, "..", "node_modules", ".cache", "e2e-download.json");
    fs.mkdirSync(path.dirname(tmp), { recursive: true });
    await download.saveAs(tmp);

    const raw = fs.readFileSync(tmp, "utf-8");
    const parsed = JSON.parse(raw) as {
      request_id?: string;
      document_type?: string;
      status?: string;
      data?: { wells?: unknown[]; rig_name?: string };
      metadata?: { source_file?: string };
    };

    expect(parsed.request_id).toBeTruthy();
    expect(parsed.document_type).toBe("well_plan");
    expect(["Success", "PartialSuccess"]).toContain(parsed.status);
    expect(parsed.data?.wells).toBeDefined();
    expect(Array.isArray(parsed.data?.wells)).toBe(true);
    expect((parsed.data?.wells ?? []).length).toBeGreaterThan(0);
    expect(parsed.data?.rig_name).toBeTruthy();
  });
});
