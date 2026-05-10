// ═══════════════════════════════════════════════════════════════════
//  Generic Data Mapper — works with ANY document type's JSON output.
//  Auto-discovers fields, tables, sections from the extraction JSON.
// ═══════════════════════════════════════════════════════════════════

import type { ExtractionResponse } from "@/services/api";
import type { ChatMessage } from "@/data/workspaceData";

export interface GenericField {
  id: string;
  key: string;
  label: string;
  value: string;
  rawValue: unknown;
  confidence: number;
  type: "string" | "number" | "currency" | "date" | "boolean" | "object";
  section: string;
}

export interface GenericTable {
  id: string;
  key: string;
  label: string;
  columns: { key: string; label: string; type: "string" | "number" | "currency" }[];
  rows: Record<string, unknown>[];
}

export interface GenericSection {
  id: string;
  key: string;
  label: string;
  fields: GenericField[];
}

export interface MappedWorkspaceData {
  documentType: string;
  documentMeta: {
    fileName: string;
    vendor: string;
    status: string;
    confidenceLevel: string;
    overallConfidence: number;
    pages: number;
    processedAt: string;
    processingTime: string;
  };
  sections: GenericSection[];
  tables: GenericTable[];
  validationIssues: { id: string; type: "error" | "warning" | "info"; label: string; detail: string }[];
  aiSummaryMessage: ChatMessage;
  pdfPages: number[];
  rawData: Record<string, unknown> | null;
}

const SKIP_KEYS = new Set(["confidence", "document_type", "language", "extraction_version", "model_confidence", "processing_notes", "depth_unit", "document_format"]);

function humanize(key: string): string {
  return key.replace(/_/g, " ").replace(/([a-z])([A-Z])/g, "$1 $2").replace(/\b\w/g, c => c.toUpperCase());
}

function isCurrency(key: string, val: unknown): boolean {
  if (typeof val !== "number") return false;
  return /amount|total|subtotal|price|cost|fee|tax|discount|balance|payment|charge/.test(key.toLowerCase());
}

function isDate(_key: string, val: unknown): boolean {
  if (typeof val !== "string") return false;
  if (/date|_at$|_on$|timestamp|created|updated|due/.test(_key.toLowerCase())) return true;
  return /^\d{4}-\d{2}-\d{2}/.test(val);
}

function fmtCurrency(val: number): string {
  return "$" + val.toLocaleString("en-US", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
}

function fmtValue(val: unknown, type: string): string {
  if (val == null || val === "") return "—";
  if (type === "currency" && typeof val === "number") return fmtCurrency(val);
  if (type === "boolean") return val ? "Yes" : "No";
  if (typeof val === "object") return JSON.stringify(val);
  return String(val);
}

function getConfidence(key: string, data: Record<string, unknown>, response: ExtractionResponse): number {
  const fc = response.field_confidences?.[key];
  if (fc) return Math.round(fc.score * 100);
  const embedded = data.confidence as Record<string, number> | undefined;
  if (embedded?.[key] != null) return Math.round(embedded[key] * 100);
  return 85;
}

function detectType(key: string, val: unknown): GenericField["type"] {
  if (typeof val === "boolean") return "boolean";
  if (isCurrency(key, val)) return "currency";
  if (typeof val === "number") return "number";
  if (isDate(key, val)) return "date";
  if (typeof val === "object" && val !== null) return "object";
  return "string";
}

function findPrimaryId(data: Record<string, unknown>): string {
  for (const c of ["vendor_name","company_name","operator","employee_name","rig_name","client_name","customer_name"]) {
    if (data[c] && typeof data[c] === "string") return data[c] as string;
  }
  for (const [k, v] of Object.entries(data)) {
    if (SKIP_KEYS.has(k)) continue;
    if (typeof v === "string" && v.length > 0 && v.length < 80) return v;
  }
  return "Document";
}

function discoverColumns(rows: Record<string, unknown>[]): GenericTable["columns"] {
  if (rows.length === 0) return [];
  const keyCounts = new Map<string, number>();
  const keyTypes = new Map<string, Set<string>>();
  for (const row of rows) {
    for (const [k, v] of Object.entries(row)) {
      if (SKIP_KEYS.has(k) || (typeof v === "object" && v !== null && !Array.isArray(v))) continue;
      keyCounts.set(k, (keyCounts.get(k) || 0) + 1);
      if (!keyTypes.has(k)) keyTypes.set(k, new Set());
      if (isCurrency(k, v)) keyTypes.get(k)!.add("currency");
      else if (typeof v === "number") keyTypes.get(k)!.add("number");
      else keyTypes.get(k)!.add("string");
    }
  }
  return Array.from(keyCounts.entries())
    .sort((a, b) => b[1] - a[1] || a[0].localeCompare(b[0]))
    .slice(0, 8)
    .map(([key]) => {
      const types = keyTypes.get(key)!;
      const type = types.has("currency") ? "currency" : types.has("number") ? "number" : "string";
      return { key, label: humanize(key), type: type as "string" | "number" | "currency" };
    });
}

export function mapExtractionToWorkspace(response: ExtractionResponse, originalFileName?: string): MappedWorkspaceData {
  const data = (response.data ?? {}) as Record<string, unknown>;
  const meta = response.metadata;
  const sections: GenericSection[] = [];
  const tables: GenericTable[] = [];
  const scalarFields: GenericField[] = [];
  let fc = 0;

  for (const [key, value] of Object.entries(data)) {
    if (SKIP_KEYS.has(key)) continue;

    // Array of objects → Table
    if (Array.isArray(value) && value.length > 0 && typeof value[0] === "object" && value[0] !== null) {
      const rows = value as Record<string, unknown>[];
      const columns = discoverColumns(rows);
      if (columns.length > 0) tables.push({ id: `tbl-${key}`, key, label: humanize(key), columns, rows });
      continue;
    }
    // Array of primitives → join
    if (Array.isArray(value)) {
      const str = value.filter(v => v != null).join(", ");
      if (str) scalarFields.push({ id: `f-${fc++}`, key, label: humanize(key), value: str, rawValue: value, confidence: getConfidence(key, data, response), type: "string", section: "details" });
      continue;
    }
    // Nested object → sub-section
    if (typeof value === "object" && value !== null) {
      const nested = value as Record<string, unknown>;
      const nf: GenericField[] = [];
      for (const [nk, nv] of Object.entries(nested)) {
        if (SKIP_KEYS.has(nk) || (typeof nv === "object" && nv !== null)) continue;
        const t = detectType(nk, nv);
        nf.push({ id: `f-${fc++}`, key: `${key}.${nk}`, label: humanize(nk), value: fmtValue(nv, t), rawValue: nv, confidence: getConfidence(nk, data, response), type: t, section: key });
      }
      if (nf.length > 0) sections.push({ id: `sec-${key}`, key, label: humanize(key), fields: nf });
      continue;
    }
    // Scalar
    if (value != null && value !== "") {
      const t = detectType(key, value);
      scalarFields.push({ id: `f-${fc++}`, key, label: humanize(key), value: fmtValue(value, t), rawValue: value, confidence: getConfidence(key, data, response), type: t, section: isCurrency(key, value) ? "financial" : "header" });
    }
  }

  const headerFields = scalarFields.filter(f => f.section !== "financial");
  const financialFields = scalarFields.filter(f => f.section === "financial");
  if (headerFields.length > 0) sections.unshift({ id: "sec-header", key: "header", label: "Extracted Fields", fields: headerFields });
  if (financialFields.length > 0) sections.push({ id: "sec-financial", key: "financial", label: "Financial", fields: financialFields });

  // Validation
  const validationIssues = (response.validation.messages ?? []).map((msg, i) => ({
    id: `v${i + 1}`,
    type: (msg.severity === "Error" ? "error" : msg.severity === "Warning" ? "warning" : "info") as "error" | "warning" | "info",
    label: msg.code?.replace(/_/g, " ").replace(/\b\w/g, c => c.toUpperCase()) ?? msg.message.slice(0, 30),
    detail: msg.message,
  }));
  response.validation.warnings?.forEach((w, i) => {
    if (!validationIssues.some(v => v.detail === w)) validationIssues.push({ id: `fw${i}`, type: "warning", label: "Warning", detail: w });
  });
  response.validation.errors?.forEach((e, i) => {
    if (!validationIssues.some(v => v.detail === e)) validationIssues.push({ id: `fe${i}`, type: "error", label: "Error", detail: e });
  });

  // AI summary
  const totalFields = sections.reduce((s, sec) => s + sec.fields.length, 0);
  const totalRows = tables.reduce((s, tbl) => s + tbl.rows.length, 0);
  const vendor = findPrimaryId(data);
  const avgConf = totalFields > 0 ? sections.flatMap(s => s.fields).reduce((s, f) => s + f.confidence, 0) / totalFields : 85;
  const parts = [
    `I've analyzed **${originalFileName || meta.source_file}**. Here's what I found:\n`,
    `- **${totalFields} fields** across ${sections.length} section${sections.length !== 1 ? "s" : ""}`,
  ];
  if (totalRows > 0) parts.push(`- **${totalRows} row${totalRows !== 1 ? "s" : ""}** in ${tables.map(t => t.label).join(", ")}`);
  parts.push(`- Average confidence: **${avgConf.toFixed(0)}%**`);
  if (response.validation.warning_count > 0) parts.push(`- ⚠️ **${response.validation.warning_count} warning(s)**`);
  if (response.validation.error_count > 0) parts.push(`- ❌ **${response.validation.error_count} error(s)**`);
  if (meta.dual_pass_triggered) parts.push(`- 🔄 **Dual-pass** triggered`);
  if (response.validation.warning_count === 0 && response.validation.error_count === 0) parts.push(`- ✅ No validation issues`);
  parts.push(`- ⏱️ ${meta.page_count} pages in **${(meta.processing_time_ms / 1000).toFixed(1)}s** (${meta.total_tokens_used} tokens)`);

  const confLevel = response.validation.confidence_score >= 0.9 ? "High Confidence" : response.validation.confidence_score >= 0.7 ? "Medium Confidence" : "Low Confidence";

  return {
    documentType: response.document_type,
    documentMeta: { fileName: originalFileName || meta.source_file, vendor, status: response.status === "Success" ? "Processed" : response.status, confidenceLevel: confLevel, overallConfidence: Math.round(response.validation.confidence_score * 100), pages: meta.page_count, processedAt: meta.extracted_at, processingTime: `${(meta.processing_time_ms / 1000).toFixed(1)}s` },
    sections, tables, validationIssues,
    aiSummaryMessage: { id: "m1", role: "ai", content: parts.join("\n"), timestamp: new Date().toLocaleTimeString("en-US", { hour: "2-digit", minute: "2-digit", hour12: false }) },
    pdfPages: Array.from({ length: meta.page_count || 1 }, (_, i) => i + 1),
    rawData: data,
  };
}
