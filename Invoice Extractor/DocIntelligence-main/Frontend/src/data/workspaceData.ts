export const documentMeta = {
  fileName: "INV-2047.pdf",
  vendor: "Acme Industrial Supply Co.",
  status: "Processed",
  confidenceLevel: "High Confidence",
  overallConfidence: 94,
  pages: 3,
  processedAt: "2026-03-21T09:42:18Z",
  processingTime: "2.4s",
};

export interface ExtractedField {
  id: string;
  label: string;
  value: string;
  confidence: number;
  section: "header" | "financial" | "meta";
  editable?: boolean;
  note?: string;
}

export const extractedFields: ExtractedField[] = [
  { id: "inv-num", label: "Invoice Number", value: "INV-2047", confidence: 99, section: "header" },
  { id: "vendor", label: "Vendor", value: "Acme Industrial Supply Co.", confidence: 97, section: "header" },
  { id: "date", label: "Invoice Date", value: "2026-03-18", confidence: 95, section: "header" },
  { id: "po-num", label: "PO Number", value: "PO-8834", confidence: 88, section: "header" },
  { id: "due-date", label: "Due Date", value: "2026-04-17", confidence: 91, section: "header" },
  { id: "subtotal", label: "Subtotal", value: "$14,280.00", confidence: 96, section: "financial" },
  { id: "tax", label: "Tax (8.5%)", value: "$1,213.80", confidence: 94, section: "financial" },
  { id: "total", label: "Total", value: "$15,493.80", confidence: 72, section: "financial", note: "Possible mismatch with line item sum" },
];

export interface LineItem {
  id: string;
  item: string;
  qty: number;
  unitPrice: number;
  total: number;
  confidence: number;
  anomaly?: string;
  aiNote?: string;
}

export const lineItems: LineItem[] = [
  { id: "li-1", item: "Steel Mounting Bracket (Type-A)", qty: 200, unitPrice: 24.50, total: 4900.00, confidence: 97 },
  { id: "li-2", item: "Industrial Fastener Kit #440", qty: 50, unitPrice: 68.00, total: 3400.00, confidence: 95 },
  { id: "li-3", item: "Hydraulic Seal Ring (12mm)", qty: 500, unitPrice: 3.80, total: 1900.00, confidence: 93 },
  { id: "li-4", item: "Precision Bearing Assembly", qty: 30, unitPrice: 89.00, total: 2670.00, confidence: 91 },
  { id: "li-5", item: "Welding Rod Bundle (E7018)", qty: 25, unitPrice: 52.40, total: 1310.00, confidence: 68, anomaly: "Unit price 18% above avg", aiNote: "Historical avg for this item is $44.30. This vendor's last 3 invoices show progressive price increases." },
  { id: "li-6", item: "Thermal Insulation Sheet", qty: 10, unitPrice: 110.00, total: 1100.00, confidence: 88 },
];

export const validationIssues = [
  { id: "v1", type: "warning" as const, label: "Total mismatch", detail: "Calculated total ($15,280.00) differs from extracted total ($15,493.80) by $213.80" },
  { id: "v2", type: "error" as const, label: "Missing approval", detail: "No approval signature detected on page 2" },
  { id: "v3", type: "info" as const, label: "Duplicate check", detail: "No duplicate invoices found in the last 90 days" },
];

export interface ChatMessage {
  id: string;
  role: "user" | "ai";
  content: string;
  timestamp: string;
}

export const initialMessages: ChatMessage[] = [
  {
    id: "m1",
    role: "ai",
    content: "I've analyzed **INV-2047.pdf** from Acme Industrial Supply. Here's what I found:\n\n- **6 line items** extracted with avg 88.8% confidence\n- ⚠️ **Total mismatch**: $213.80 discrepancy detected\n- ⚠️ Line item #5 has an **anomalous unit price** (18% above historical avg)\n- ✅ No duplicate invoices in the system\n\nWould you like me to investigate any of these findings?",
    timestamp: "09:42",
  },
];

/** Invoice / generic finance-style prompts */
export const aiQuickActions = [
  "Summarize document",
  "Find anomalies",
  "Compare with PO",
  "Explain totals",
  "Check duplicates",
];

/** Well plan / drilling program — uses same chat API (JSON-only context) */
export const wellPlanAiQuickActions = [
  "Summarize risks and mitigations from the extraction",
  "List target formation and TD (MD/TVD) for each well",
  "Describe the casing program by hole size and depth intervals",
  "What mud weights and fluid types are specified per section?",
  "What data looks incomplete or worth double-checking?",
];

export const pdfPages = [1, 2, 3];
