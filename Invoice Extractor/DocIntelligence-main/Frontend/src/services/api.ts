// ═══════════════════════════════════════════════════════════════════
//  DocIQ API Service — Full CRUD + Extraction
//
//  READ endpoints:
//    GET  /api/v1/document-types              → list all types
//    GET  /api/v1/document-types/{id}         → get type detail
//    GET  /api/v1/document-types/{id}/prompts → get prompts (admin)
//
//  CRUD endpoints:
//    POST   /api/v1/document-types            → create type
//    PUT    /api/v1/document-types/{id}       → update type
//    DELETE /api/v1/document-types/{id}       → delete type
//
//  EXTRACTION endpoints:
//    POST /api/v1/extraction/extract          → single document
//    POST /api/v1/document-types/{id}/test-extract → test with sample
//    GET  /api/v1/extraction/jobs/{jobId}     → async poll
// ═══════════════════════════════════════════════════════════════════

const getBaseUrl = (): string =>
  localStorage.getItem("dociq_api_url") ||
  import.meta.env.VITE_API_BASE_URL ||
  "/api";

const getApiKey = (): string =>
  localStorage.getItem("dociq_api_key") ||
  import.meta.env.VITE_API_KEY ||
  "dev-key-replace-in-production";

function headers(): Record<string, string> {
  const h: Record<string, string> = { Accept: "application/json" };
  const key = getApiKey();
  if (key) h["X-Api-Key"] = key;
  return h;
}

function jsonHeaders(): Record<string, string> {
  return { ...headers(), "Content-Type": "application/json" };
}

// ── Common Types ────────────────────────────────────────────────

export interface FieldConfidence {
  score: number;
  source_pass: string | null;
  tier: "high" | "medium" | "low";
}

export interface ValidationMessage {
  field: string;
  severity: "Error" | "Warning" | "Info";
  code: string;
  message: string;
  suggested_action?: string;
}

export interface ValidationSummary {
  is_valid: boolean;
  confidence_score: number;
  warnings: string[];
  errors: string[];
  warning_count: number;
  error_count: number;
  messages?: ValidationMessage[];
}

export interface ExtractionMetadata {
  source_file: string;
  file_size_bytes: number;
  page_count: number;
  extraction_method: string;
  model_used: string;
  reasoning_effort: string;
  dual_pass_triggered: boolean;
  extracted_at: string;
  processing_time_ms: number;
  total_tokens_used: number;
  pages_sent_to_model: number;
}

export interface ExtractionResponse {
  request_id: string;
  document_type: string;
  status: "Success" | "PartialSuccess" | "Failed" | "Queued" | "Processing";
  metadata: ExtractionMetadata;
  data: Record<string, unknown> | null;
  validation: ValidationSummary;
  field_confidences?: Record<string, FieldConfidence>;
  error?: { code: string; message: string; details?: string };
}

export type JobStage =
  | "Uploaded" | "Preprocessing" | "Extracting" | "Validating"
  | "Exporting" | "Complete" | "Failed";

export interface ExtractionJob {
  job_id: string;
  status: "Success" | "PartialSuccess" | "Failed" | "Queued" | "Processing";
  stage: JobStage;
  current_message?: string;
  total_files: number;
  processed_files: number;
  failed_files: number;
  progress_percent: number;
  document_type?: string;
  result_available: boolean;
  started_at_utc: string;
  updated_at_utc: string;
  completed_at_utc?: string;
  elapsed_ms: number;
  error?: { code: string; message: string; details?: string };
  results?: ExtractionResponse[];
}

export interface ExtractOptions {
  documentType?: string;
  reasoningEffort?: string;
  enableDualPass?: boolean;
  additionalContext?: string;
}

// ── Document Type Types ─────────────────────────────────────────

export interface DocumentTypeSummary {
  key: string;
  display_name: string;
  description: string;
  version: string;
  enabled: boolean;
  accepted_file_types: string[];
  max_file_size_mb: number;
  max_page_count: number;
  icon_name?: string;
  category?: string;
  supports_batch: boolean;
  supports_excel_export: boolean;
  supports_json_export: boolean;
  dual_pass_enabled: boolean;
  sample_fields: string[];
}

export interface DocumentTypeDetail extends DocumentTypeSummary {
  extraction_settings: {
    max_pages_for_vision: number;
    image_dpi: number;
    image_max_width_px: number;
    reasoning_effort: string;
    max_tokens: number;
    temperature: number;
  };
  dual_pass: {
    enabled: boolean;
    critical_fields: string[];
    confidence_threshold: number;
    confidence_path: string;
  };
  output: {
    include_metadata: boolean;
    include_raw_text: boolean;
    indent_json: boolean;
    excel_export_enabled: boolean;
  };
  validation_rule_count: number;
}

export interface DocumentTypePrompts {
  type_id: string;
  system_prompt: string;
  extraction_prompt: string;
  json_schema: string;
  validation_rules: ValidationRule[];
}

export interface ValidationRule {
  id: string;
  severity: "Info" | "Warning" | "Error";
  type: string;
  field: string;
  pattern?: string;
  message: string;
  confidence_penalty: number;
}

export interface CreateDocumentTypeRequest {
  type_id: string;
  display_name?: string;
  description?: string;
  version?: string;
  enabled?: boolean;
  category?: string;
  icon_name?: string;
  accepted_extensions?: string[];
  max_file_size_mb?: number;
  max_pages?: number;
  system_prompt?: string;
  extraction_prompt?: string;
  json_schema?: string;
  validation_rules?: ValidationRule[];
  reasoning_effort?: string;
  max_tokens?: number;
  max_pages_for_vision?: number;
  dual_pass_enabled?: boolean;
  dual_pass_critical_fields?: string[];
  excel_export_enabled?: boolean;
}

export interface UpdateDocumentTypeRequest {
  display_name?: string;
  description?: string;
  version?: string;
  enabled?: boolean;
  category?: string;
  icon_name?: string;
  accepted_extensions?: string[];
  max_file_size_mb?: number;
  max_pages?: number;
  system_prompt?: string;
  extraction_prompt?: string;
  json_schema?: string;
  validation_rules?: ValidationRule[];
}

// ═══════════════════════════════════════════════════════════════════
//  Document Type CRUD API
// ═══════════════════════════════════════════════════════════════════

export async function fetchDocumentTypes(): Promise<DocumentTypeSummary[]> {
  const res = await fetch(`${getBaseUrl()}/v1/document-types`, { headers: headers() });
  if (!res.ok) throw new Error(`Failed to fetch document types: ${res.status}`);
  return res.json();
}

export async function fetchDocumentType(typeId: string): Promise<DocumentTypeDetail> {
  const res = await fetch(`${getBaseUrl()}/v1/document-types/${typeId}`, { headers: headers() });
  if (!res.ok) throw new Error(`Failed to fetch document type: ${res.status}`);
  return res.json();
}

export async function fetchDocumentTypePrompts(typeId: string): Promise<DocumentTypePrompts> {
  const res = await fetch(`${getBaseUrl()}/v1/document-types/${typeId}/prompts`, { headers: headers() });
  if (!res.ok) throw new Error(`Failed to fetch prompts: ${res.status}`);
  return res.json();
}

export async function createDocumentType(request: CreateDocumentTypeRequest): Promise<DocumentTypeSummary> {
  const res = await fetch(`${getBaseUrl()}/v1/document-types`, {
    method: "POST",
    headers: jsonHeaders(),
    body: JSON.stringify(request),
  });
  if (!res.ok) {
    const err = await res.json().catch(() => ({}));
    throw new Error(err.detail || `Create failed: ${res.status}`);
  }
  return res.json();
}

export async function updateDocumentType(typeId: string, request: UpdateDocumentTypeRequest): Promise<DocumentTypeSummary> {
  const res = await fetch(`${getBaseUrl()}/v1/document-types/${typeId}`, {
    method: "PUT",
    headers: jsonHeaders(),
    body: JSON.stringify(request),
  });
  if (!res.ok) {
    const err = await res.json().catch(() => ({}));
    throw new Error(err.detail || `Update failed: ${res.status}`);
  }
  return res.json();
}

export async function deleteDocumentType(typeId: string): Promise<void> {
  const res = await fetch(`${getBaseUrl()}/v1/document-types/${typeId}`, {
    method: "DELETE",
    headers: headers(),
  });
  if (!res.ok) throw new Error(`Delete failed: ${res.status}`);
}

export async function reloadDocumentTypes(): Promise<{ types_before: number; types_after: number; types: string[] }> {
  const res = await fetch(`${getBaseUrl()}/v1/document-types/reload`, {
    method: "POST",
    headers: headers(),
  });
  if (!res.ok) throw new Error(`Reload failed: ${res.status}`);
  return res.json();
}

export async function testExtract(typeId: string, file: File): Promise<ExtractionResponse> {
  const form = new FormData();
  form.append("file", file);
  const res = await fetch(`${getBaseUrl()}/v1/document-types/${typeId}/test-extract`, {
    method: "POST",
    headers: headers(),
    body: form,
  });
  if (!res.ok) {
    const err = await res.text().catch(() => "");
    throw new Error(err || `Test extraction failed: ${res.status}`);
  }
  return res.json();
}

// ═══════════════════════════════════════════════════════════════════
//  Extraction API (existing — unchanged)
// ═══════════════════════════════════════════════════════════════════

export async function extractDocument(
  file: File,
  opts: ExtractOptions = {}
): Promise<ExtractionResponse> {
  const form = new FormData();
  form.append("file", file);
  form.append("documentType", opts.documentType || "invoice");
  if (opts.reasoningEffort || opts.enableDualPass != null || opts.additionalContext) {
    form.append(
      "optionsJson",
      JSON.stringify({
        ReasoningEffort: opts.reasoningEffort,
        EnableDualPass: opts.enableDualPass,
        AdditionalContext: opts.additionalContext,
      })
    );
  }

  const url = `${getBaseUrl()}/v1/extraction/extract`;
  const res = await fetch(url, { method: "POST", headers: headers(), body: form });

  if (!res.ok) {
    const errBody = await res.text().catch(() => "");
    let parsed: Record<string, unknown> = {};
    try { parsed = JSON.parse(errBody); } catch { /* not JSON */ }
    const detail = (parsed.detail as string) || (parsed.title as string) || errBody || `Extraction failed: ${res.status}`;
    throw new Error(detail);
  }
  return res.json();
}

export async function pollJob(jobId: string): Promise<ExtractionJob> {
  const res = await fetch(`${getBaseUrl()}/v1/extraction/jobs/${jobId}`, { headers: headers() });
  if (!res.ok) throw new Error(`Job poll failed: ${res.status}`);
  return res.json();
}

/** MVP: ask questions using only extracted JSON (no PDF). Uses server store by request_id, or inline data for demo/offline. */
export async function extractionChat(payload: {
  request_id?: string;
  message: string;
  data?: Record<string, unknown> | null;
  document_type?: string;
  source_file?: string;
}): Promise<{ reply: string }> {
  const body = {
    request_id: payload.request_id,
    message: payload.message,
    data: payload.data ?? undefined,
    document_type: payload.document_type,
    source_file: payload.source_file,
  };
  const res = await fetch(`${getBaseUrl()}/v1/extraction/chat`, {
    method: "POST",
    headers: jsonHeaders(),
    body: JSON.stringify(body),
    signal: AbortSignal.timeout(180_000),
  });
  if (!res.ok) {
    const text = await res.text().catch(() => "");
    let detail = text;
    try {
      const j = JSON.parse(text) as { detail?: string; title?: string };
      detail = j.detail || j.title || text;
    } catch {
      /* use text */
    }
    throw new Error(detail || `Chat failed: ${res.status}`);
  }
  return res.json();
}

/** Submit user-approved edits to the extraction. Body is the edited extraction JSON. */
export async function approveExtraction(
  payload: Record<string, unknown>
): Promise<{ ok: boolean; status: number; body: unknown }> {
  const res = await fetch(`${getBaseUrl()}/v1/extraction/approve`, {
    method: "POST",
    headers: jsonHeaders(),
    body: JSON.stringify(payload),
  });
  const text = await res.text().catch(() => "");
  let body: unknown = text;
  try { body = JSON.parse(text); } catch { /* keep text */ }
  if (!res.ok) {
    const detail =
      (body && typeof body === "object" && "detail" in (body as Record<string, unknown>)
        ? String((body as Record<string, unknown>).detail)
        : text) || `Approve failed: ${res.status}`;
    throw new Error(detail);
  }
  return { ok: true, status: res.status, body };
}

// ── Demo mode ────────────────────────────────────────────────────
export function isDemoMode(): boolean {
  if (localStorage.getItem("dociq_demo_mode") === "true") return true;
  if (import.meta.env.VITE_DEMO_MODE === "true") return true;
  return false;
}

// Demo document types for when running without backend
export function getDemoDocumentTypes(): DocumentTypeSummary[] {
  return [
    {
      key: "invoice", display_name: "AP Invoice",
      description: "Extract vendor, amounts, line items, PO references from accounts payable invoices",
      version: "6.0", enabled: true, accepted_file_types: [".pdf", ".png", ".jpg"],
      max_file_size_mb: 50, max_page_count: 30, icon_name: "receipt", category: "Finance",
      supports_batch: true, supports_excel_export: true, supports_json_export: true,
      dual_pass_enabled: true,
      sample_fields: ["vendor_name", "invoice_number", "invoice_date", "total_amount", "line_items"],
    },
    {
      key: "purchase_order", display_name: "Purchase Order",
      description: "Extract PO details, line items, shipping info, terms and conditions",
      version: "2.0", enabled: true, accepted_file_types: [".pdf"],
      max_file_size_mb: 50, max_page_count: 20, icon_name: "shopping-cart", category: "Procurement",
      supports_batch: true, supports_excel_export: true, supports_json_export: true,
      dual_pass_enabled: true,
      sample_fields: ["po_number", "vendor_name", "order_date", "total_amount", "line_items"],
    },
    {
      key: "timesheet", display_name: "Timesheet",
      description: "Extract employee hours, overtime, pay period data from timesheets",
      version: "1.5", enabled: true, accepted_file_types: [".pdf", ".png", ".jpg"],
      max_file_size_mb: 25, max_page_count: 10, icon_name: "clock", category: "HR",
      supports_batch: true, supports_excel_export: true, supports_json_export: true,
      dual_pass_enabled: false,
      sample_fields: ["employee_name", "pay_period", "regular_hours", "overtime_hours", "total_hours"],
    },
    {
      key: "toursheet", display_name: "Tour Sheet",
      description: "Extract drilling tour/shift report data — depths, operations, mud parameters",
      version: "1.0", enabled: true, accepted_file_types: [".pdf"],
      max_file_size_mb: 50, max_page_count: 15, icon_name: "clipboard", category: "Operations",
      supports_batch: true, supports_excel_export: true, supports_json_export: true,
      dual_pass_enabled: true,
      sample_fields: ["rig_number", "tour_date", "tour_type", "depth_start", "depth_end"],
    },
    {
      key: "well_plan", display_name: "Well Plan / Drilling Program",
      description: "Extract wells, formations, casing, BHAs, parameters, risks from drilling programs",
      version: "1.0", enabled: true, accepted_file_types: [".pdf"],
      max_file_size_mb: 100, max_page_count: 50, icon_name: "target", category: "Field Operations",
      supports_batch: false, supports_excel_export: false, supports_json_export: true,
      dual_pass_enabled: false,
      sample_fields: ["rig_name", "wells", "formation_tops", "casing_program", "bha"],
    },
  ];
}

export async function extractDocumentDemo(
  file: File,
  _onProgress?: (stage: JobStage, pct: number, msg: string) => void,
  documentType?: string
): Promise<ExtractionResponse> {
  await new Promise((r) => setTimeout(r, 2000));

  if (documentType === "well_plan" || file.name.toLowerCase().includes("well") || file.name.toLowerCase().includes("plan")) {
    const { WELL_PLAN_DEMO_DATA } = await import("@/data/wellPlanDemoData");
    return {
      request_id: crypto.randomUUID().replace(/-/g, ""),
      document_type: "well_plan", status: "Success",
      metadata: {
        source_file: file.name, file_size_bytes: file.size, page_count: 20,
        extraction_method: "vision", model_used: "gpt-4o", reasoning_effort: "high",
        dual_pass_triggered: false, extracted_at: new Date().toISOString(),
        processing_time_ms: 47200, total_tokens_used: 38500, pages_sent_to_model: 5,
      },
      data: WELL_PLAN_DEMO_DATA,
      validation: { is_valid: true, confidence_score: 0.96, warnings: [], errors: [], warning_count: 0, error_count: 0, messages: [] },
      field_confidences: { rig_name: { score: 0.99, source_pass: "primary", tier: "high" } },
    };
  }

  await new Promise((r) => setTimeout(r, 1500));
  return {
    request_id: crypto.randomUUID().replace(/-/g, ""),
    document_type: documentType || "invoice", status: "Success",
    metadata: {
      source_file: file.name, file_size_bytes: file.size, page_count: 3,
      extraction_method: "vision", model_used: "gpt-4o", reasoning_effort: "medium",
      dual_pass_triggered: true, extracted_at: new Date().toISOString(),
      processing_time_ms: 2400, total_tokens_used: 2847, pages_sent_to_model: 3,
    },
    data: {
      vendor_name: "Acme Industrial Supply Co.", invoice_number: "INV-2047",
      invoice_date: "2026-03-18", due_date: "2026-04-17", po_number: "PO-8834",
      currency: "USD", subtotal: 14280.0, tax_amount: 1213.8, total_amount: 15493.8,
    },
    validation: {
      is_valid: true, confidence_score: 0.92, warnings: [], errors: [],
      warning_count: 0, error_count: 0, messages: [],
    },
    field_confidences: {
      vendor_name: { score: 0.97, source_pass: "primary", tier: "high" },
      invoice_number: { score: 0.99, source_pass: "primary", tier: "high" },
    },
  };
}
