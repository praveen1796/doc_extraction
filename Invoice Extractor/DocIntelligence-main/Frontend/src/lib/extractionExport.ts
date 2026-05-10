import type { ExtractionResponse } from "@/services/api";
import { downloadStructuredExtractionXlsx } from "@/lib/structuredExtractionXlsx";
import { downloadWellPlanXlsx } from "@/lib/wellPlanXlsx";

/** Safe filename stem from uploaded file name */
export function extractionJsonBaseName(fileName: string): string {
  const base = fileName.replace(/[/\\?%*:|"<>]/g, "_").replace(/\.[^.]+$/, "");
  return base || "extraction";
}

export function downloadJson(filename: string, value: unknown): void {
  const json = JSON.stringify(value, null, 2);
  const blob = new Blob([json], { type: "application/json;charset=utf-8" });
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = filename.toLowerCase().endsWith(".json") ? filename : `${filename}.json`;
  document.body.appendChild(a);
  a.click();
  a.remove();
  URL.revokeObjectURL(url);
}

export async function copyJsonToClipboard(value: unknown): Promise<void> {
  await navigator.clipboard.writeText(JSON.stringify(value, null, 2));
}

/** Payload matching API shape — suitable for archive / re-processing */
export function buildFullExtractionExport(res: ExtractionResponse): Record<string, unknown> {
  return {
    request_id: res.request_id,
    document_type: res.document_type,
    status: res.status,
    metadata: res.metadata,
    data: res.data,
    validation: res.validation,
    field_confidences: res.field_confidences,
    error: res.error,
  };
}

/**
 * Download .xlsx: `well_plan` → `wellPlanXlsx` (dedicated well sheets only).
 * All other types → `structuredExtractionXlsx` (summary + table sheets) — never mixed with well plan.
 */
export function downloadExtractionXlsx(res: ExtractionResponse, fileName: string): void {
  if (!res.data) return;
  const stem = extractionJsonBaseName(fileName);
  const dt = (res.document_type ?? "").toLowerCase().replace(/-/g, "_");
  if (dt === "well_plan" || dt === "wellplan") {
    downloadWellPlanXlsx(stem, res.data as Record<string, unknown>);
    return;
  }
  downloadStructuredExtractionXlsx(stem, res.document_type || "extraction", res.data as Record<string, unknown>);
}

/** Tooltip for the spreadsheet download button (well plan vs contract vs other). */
export function extractionXlsxButtonTitle(documentType: string | undefined | null): string {
  const dt = (documentType ?? "").toLowerCase().replace(/-/g, "_");
  if (dt === "well_plan" || dt === "wellplan") {
    return "Download well plan Excel (.xlsx) — Summary, Wells, Formations, Casing, Drilling, Fluids, Risks";
  }
  if (dt === "contract" || dt === "contracts") {
    return "Download contract Excel (.xlsx) — Contract summary + one sheet per line-item / schedule table";
  }
  return "Download table Excel (.xlsx) — Field summary + one sheet per array of objects";
}
