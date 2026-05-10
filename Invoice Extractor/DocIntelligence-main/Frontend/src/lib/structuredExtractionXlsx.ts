/**
 * Client-side Excel for non–well_plan extractions (contract, invoice, PO, etc.).
 * Intentionally separate from `wellPlanXlsx.ts` so well plan layout is never affected.
 *
 * Produces: Summary (Field | Value) + one sheet per top-level array of objects.
 */
import * as XLSX from "xlsx";

type JsonObj = Record<string, unknown>;

function str(v: unknown): string {
  if (v == null) return "";
  if (typeof v === "string") return v;
  if (typeof v === "number" || typeof v === "boolean") return String(v);
  return JSON.stringify(v);
}

function sheetName(name: string): string {
  const n = name.replace(/[/\\?*[\]:]/g, "_");
  return n.length <= 31 ? n : n.slice(0, 31);
}

/** Field order for contract/agreement summary rows (then remaining keys A→Z) */
const CONTRACT_SUMMARY_KEY_PRIORITY: string[] = [
  "document_type",
  "contract_id",
  "ContractId",
  "contract_title",
  "ContractName",
  "contract_type",
  "operator_name",
  "contractor_name",
  "reference_number",
  "Currency",
  "currency",
  "TotalAmount",
  "total_amount",
  "TotalContractValue",
  "effective_date",
  "EffectiveDate",
  "commencement_date",
  "CommencementDate",
  "expiration_date",
  "ExpirationDate",
  "docusign_envelope_id",
  "governing_law",
  "venue",
  "master_agreement_reference",
];

function sortSummaryFieldKeys(fieldKeys: string[], isContract: boolean): string[] {
  if (!isContract) {
    return [...fieldKeys].sort((a, b) => a.localeCompare(b, undefined, { sensitivity: "base" }));
  }
  const pr = (k: string) => {
    const i = CONTRACT_SUMMARY_KEY_PRIORITY.indexOf(k);
    return i === -1 ? 999 + k.charCodeAt(0) : i;
  };
  return [...fieldKeys].sort((a, b) => {
    const d = pr(a) - pr(b);
    if (d !== 0) return d;
    return a.localeCompare(b, undefined, { sensitivity: "base" });
  });
}

/** Table column order for line items, rates, and commercial line arrays */
const LINE_TABLE_HEADER_PREFERRED: string[] = [
  "ItemNumber",
  "item_number",
  "line_number",
  "section",
  "Description",
  "description",
  "Quantity",
  "quantity",
  "Unit",
  "unit",
  "UnitPrice",
  "unit_price",
  "unit_price_text",
  "TotalPrice",
  "total_price",
  "total",
  "amount",
  "Rate",
  "rate",
  "amount_text",
  "Notes",
  "notes",
  "PageRef",
  "page_ref",
  "Page",
  "page",
  "exhibit",
];

function collectAllObjectKeys(objects: JsonObj[]): string[] {
  const s = new Set<string>();
  for (const o of objects) {
    for (const k of Object.keys(o)) s.add(k);
  }
  return Array.from(s);
}

function orderTableHeaders(keys: string[]): string[] {
  const set = new Set(keys);
  const out: string[] = [];
  for (const p of LINE_TABLE_HEADER_PREFERRED) {
    if (set.has(p)) {
      out.push(p);
      set.delete(p);
    }
  }
  return [...out, ...Array.from(set).sort((a, b) => a.localeCompare(b, undefined, { sensitivity: "base" }))];
}

function objectArrayToAoA(objects: JsonObj[]): (string | number | boolean)[][] {
  if (objects.length === 0) {
    return [["(no rows)"]];
  }
  const rawKeys = collectAllObjectKeys(objects);
  if (rawKeys.length === 0) {
    return [
      ["Note", "Value"],
      ["(empty objects — no properties)", `Row count: ${objects.length}`],
    ];
  }
  const headers = orderTableHeaders(rawKeys);
  const headerRow: (string | number | boolean)[] = headers;
  const dataRows: (string | number | boolean)[][] = [headerRow];
  for (const o of objects) {
    const row: (string | number | boolean)[] = headers.map((h) => {
      if (!(h in o)) return "";
      const v = o[h];
      if (v == null) return "";
      if (typeof v === "number" || typeof v === "boolean") return v;
      if (typeof v === "string") return v;
      if (Array.isArray(v) || (typeof v === "object" && v !== null)) {
        return JSON.stringify(v);
      }
      return String(v);
    });
    dataRows.push(row);
  }
  return dataRows;
}

function prettySheetNameFromKey(key: string, used: Set<string>): string {
  const base = key
    .replace(/([a-z0-9])([A-Z])/g, "$1 $2")
    .replace(/_/g, " ")
    .replace(/\s+/g, " ")
    .trim();
  const name = base.length > 0 ? base : key;
  let s = sheetName(name);
  let n = 1;
  while (used.has(s)) {
    s = sheetName(`${name} (${n})`);
    n++;
  }
  used.add(s);
  return s;
}

/**
 * Workbook: Summary (Field | Value) + one table sheet per top-level array of objects
 * (line_items, Items, signatures, etc.).
 */
export function downloadStructuredExtractionXlsx(
  fileStem: string,
  documentType: string,
  data: JsonObj
): void {
  const wb = XLSX.utils.book_new();
  const usedSheetNames = new Set<string>();
  const dt = (documentType || "extraction").toLowerCase().replace(/-/g, "_");
  const isContract = dt === "contract" || dt === "contracts";

  const allKeys = Object.keys(data);
  const summaryKeys = sortSummaryFieldKeys(allKeys, isContract);
  const summaryRows: (string | number)[][] = [["Field", "Value"]];

  for (const k of summaryKeys) {
    const v = data[k as keyof JsonObj] as unknown;
    if (v == null) {
      summaryRows.push([k, ""]);
      continue;
    }
    if (Array.isArray(v)) {
      if (v.length === 0) {
        summaryRows.push([k, "(no rows)"]);
        continue;
      }
      if (typeof v[0] === "object" && v[0] !== null) {
        summaryRows.push([k, `— see table on sheet: ${k.replace(/_/g, " ")}`]);
        continue;
      }
      summaryRows.push([k, v.map((x) => str(x)).join(" · ")]);
      continue;
    }
    if (typeof v === "object") {
      const entries = Object.entries(v as object);
      if (entries.length === 0) {
        summaryRows.push([k, "{}"]);
      } else if (entries.length <= 4 && !entries.some(([, val]) => typeof val === "object" && val !== null)) {
        const flat = entries.map(([ek, ev]) => `${ek}: ${str(ev)}`).join(" | ");
        summaryRows.push([k, flat]);
      } else {
        summaryRows.push([k, JSON.stringify(v, null, 2)]);
      }
      continue;
    }
    if (typeof v === "string" || typeof v === "number" || typeof v === "boolean") {
      summaryRows.push([k, v]);
    } else {
      summaryRows.push([k, str(v)]);
    }
  }

  const summarySheetName = isContract ? "Contract summary" : "Summary";
  const sumWs = XLSX.utils.aoa_to_sheet(summaryRows);
  sumWs["!cols"] = [{ wch: 32 }, { wch: 72 }];
  const sn = prettySheetNameFromKey(summarySheetName, usedSheetNames);
  XLSX.utils.book_append_sheet(wb, sumWs, sn);

  for (const [k, v] of Object.entries(data)) {
    if (!Array.isArray(v) || v.length === 0) continue;
    if (typeof v[0] !== "object" || v[0] === null) continue;
    const objects = v as JsonObj[];
    const aoa = objectArrayToAoA(objects);
    const ws = XLSX.utils.aoa_to_sheet(aoa);
    const headers = aoa[0] as string[];
    const maxW = 52;
    ws["!cols"] = headers.map((h, i) => ({
      wch: i === 0
        ? Math.min(maxW, Math.max(10, String(h).length + 1))
        : Math.min(
            maxW,
            Math.max(12, String(h).length + 2, ...aoa.slice(1).map((r) => String((r as unknown[])[i] ?? "").length + 1)),
          ),
    }));
    const sname = prettySheetNameFromKey(k, usedSheetNames);
    XLSX.utils.book_append_sheet(wb, ws, sname);
  }

  const fileLabel = (documentType || "extraction").replace(/[\\/:*?"<>|]/g, "-");
  XLSX.writeFile(wb, `${fileStem}-${fileLabel}.xlsx`);
}

/** Contract-only: same engine, forces contract field ordering in the summary sheet. */
export function downloadContractExtractionXlsx(fileStem: string, data: JsonObj): void {
  downloadStructuredExtractionXlsx(fileStem, "contract", data);
}
