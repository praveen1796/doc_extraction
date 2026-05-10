/**
 * Well plan (.xlsx) only — multi-sheet Wells, Formations, Casing, etc.
 * Other document types use `structuredExtractionXlsx.ts` (do not add non–well-plan exports here).
 */
import * as XLSX from "xlsx";

type JsonObj = Record<string, unknown>;

function str(v: unknown): string {
  if (v == null) return "";
  if (typeof v === "string") return v;
  if (typeof v === "number" || typeof v === "boolean") return String(v);
  return JSON.stringify(v);
}

function numOrStr(v: unknown): string | number {
  if (v == null) return "";
  if (typeof v === "number") return v;
  if (typeof v === "string" && v.trim() !== "" && !Number.isNaN(Number(v))) return Number(v);
  return str(v);
}

function sheetName(name: string): string {
  const n = name.replace(/[/\\?*[\]:]/g, "_");
  return n.length <= 31 ? n : n.slice(0, 31);
}

function getChild(row: JsonObj, primary: string, alt?: string): unknown {
  if (primary in row) return row[primary];
  if (alt && alt in row) return row[alt];
  return "";
}

const casingAlt: Record<string, string | undefined> = {
  start_md: "start_md_ft",
  end_md: "end_md_ft",
  hole_size: "hole_size_in",
  casing_od: "casing_od_in",
  weight_per_length: "weight_lbm_ft",
};

const drillingAlt: Record<string, string | undefined> = {
  depth_from: "depth_from_ft",
  depth_to: "depth_to_ft",
  hole_size: "hole_size_in",
  flow_rate: "flow_rate_gpm",
  rop: "rop_fth",
  diffp_max: "diffp_max_psi",
  wob: "wob_klbf",
};

/** Multi-sheet workbook from well_plan `data` object — mirrors server Excel layout. */
export function downloadWellPlanXlsx(fileStem: string, data: JsonObj): void {
  const wb = XLSX.utils.book_new();
  const wells = Array.isArray(data.wells) ? (data.wells as JsonObj[]) : [];

  // ── Summary ──
  const summary: (string | number)[][] = [["Field", "Value"]];
  const topKeys = [
    "rig_name",
    "operator",
    "pad_name",
    "location",
    "report_date",
    "report_status",
    "depth_unit",
    "document_format",
    "language",
    "document_type",
  ];
  for (const k of topKeys) {
    if (data[k] !== undefined && data[k] !== null) summary.push([k, str(data[k])]);
  }
  summary.push(["well_count", wells.length]);
  XLSX.utils.book_append_sheet(wb, XLSX.utils.aoa_to_sheet(summary), sheetName("Summary"));

  // ── Approvals ──
  const approvals = Array.isArray(data.approvals) ? (data.approvals as JsonObj[]) : [];
  const apprRows: (string | number)[][] = [["name", "action", "datetime"]];
  for (const a of approvals) {
    apprRows.push([str(a.name), str(a.action), str(a.datetime)]);
  }
  if (apprRows.length === 1) apprRows.push(["(none)", "", ""]);
  XLSX.utils.book_append_sheet(wb, XLSX.utils.aoa_to_sheet(apprRows), sheetName("Approvals"));

  // ── Wells overview ──
  const woHeaders = [
    "well_name",
    "well_type",
    "api_number",
    "afe_number",
    "operator_well_id",
    "permit_id",
    "target_formation",
    "design",
    "total_depth_md",
    "total_depth_tvd",
    "lateral_length",
    "surface_coordinates",
    "coordinate_system",
    "ground_level",
    "rkb",
    "skid_order",
  ];
  const woRows: (string | number)[][] = [woHeaders];
  for (const w of wells) {
    woRows.push(
      woHeaders.map((h) => {
        if (h === "total_depth_md") return str(getChild(w, "total_depth_md", "total_depth_md_ft"));
        if (h === "total_depth_tvd") return str(getChild(w, "total_depth_tvd", "total_depth_tvd_ft"));
        return str(w[h]);
      })
    );
  }
  if (woRows.length === 1) woRows.push(["(no wells)", ...Array(woHeaders.length - 1).fill("")]);
  XLSX.utils.book_append_sheet(wb, XLSX.utils.aoa_to_sheet(woRows), sheetName("Wells_overview"));

  // ── Formations ──
  const fmRows: (string | number)[][] = [["well_name", "formation_name", "md", "tvd"]];
  for (const w of wells) {
    const wn = str(w.well_name);
    const tops = Array.isArray(w.formation_tops) ? (w.formation_tops as JsonObj[]) : [];
    for (const t of tops) {
      fmRows.push([wn, str(t.formation_name), numOrStr(t.md), numOrStr(t.tvd)]);
    }
  }
  if (fmRows.length === 1) fmRows.push(["(no formation tops)", "", "", ""]);
  XLSX.utils.book_append_sheet(wb, XLSX.utils.aoa_to_sheet(fmRows), sheetName("Formations"));

  // ── Casing ──
  const csHeaders = [
    "well_name",
    "section_name",
    "hole_size",
    "casing_od",
    "casing_id",
    "grade",
    "weight_per_length",
    "connection",
    "start_md",
    "end_md",
    "cement_type",
    "cement_details",
  ];
  const csRows: (string | number)[][] = [csHeaders];
  for (const w of wells) {
    const wn = str(w.well_name);
    const rows = Array.isArray(w.casing_program) ? (w.casing_program as JsonObj[]) : [];
    for (const row of rows) {
      csRows.push([
        wn,
        ...csHeaders.slice(1).map((h) => str(getChild(row, h, casingAlt[h]))),
      ]);
    }
  }
  if (csRows.length === 1) csRows.push(["(no casing)", ...Array(csHeaders.length - 1).fill("")]);
  XLSX.utils.book_append_sheet(wb, XLSX.utils.aoa_to_sheet(csRows), sheetName("Casing"));

  // ── Drilling sections ──
  const dsHeaders = [
    "well_name",
    "section_name",
    "hole_size",
    "depth_from",
    "depth_to",
    "interval",
    "wob",
    "rpm",
    "flow_rate",
    "rop",
    "diffp_max",
    "bha_type",
    "primary_bit",
    "backup_bit",
    "comments",
  ];
  const dsRows: (string | number)[][] = [dsHeaders];
  for (const w of wells) {
    const wn = str(w.well_name);
    const rows = Array.isArray(w.drilling_sections) ? (w.drilling_sections as JsonObj[]) : [];
    for (const row of rows) {
      dsRows.push([
        wn,
        ...dsHeaders.slice(1).map((h) => str(getChild(row, h, drillingAlt[h]))),
      ]);
    }
  }
  if (dsRows.length === 1) dsRows.push(["(no drilling sections)", ...Array(dsHeaders.length - 1).fill("")]);
  XLSX.utils.book_append_sheet(wb, XLSX.utils.aoa_to_sheet(dsRows), sheetName("Drilling_sections"));

  // ── Fluids ──
  const flHeaders = ["well_name", "section", "fluid_type", "design_mw", "min_mw", "max_mw", "min_fit", "mudloggers", "comments"];
  const flRows: (string | number)[][] = [flHeaders];
  for (const w of wells) {
    const wn = str(w.well_name);
    const rows = Array.isArray(w.drilling_fluids) ? (w.drilling_fluids as JsonObj[]) : [];
    for (const row of rows) {
      flRows.push([wn, ...flHeaders.slice(1).map((h) => str(row[h]))]);
    }
  }
  if (flRows.length === 1) flRows.push(["(no fluids)", ...Array(flHeaders.length - 1).fill("")]);
  XLSX.utils.book_append_sheet(wb, XLSX.utils.aoa_to_sheet(flRows), sheetName("Fluids"));

  // ── Risks ──
  const rkHeaders = ["well_name", "section", "risk", "comments"];
  const rkRows: (string | number)[][] = [rkHeaders];
  for (const w of wells) {
    const wn = str(w.well_name);
    const rows = Array.isArray(w.risks_and_hazards) ? (w.risks_and_hazards as JsonObj[]) : [];
    for (const row of rows) {
      rkRows.push([wn, str(row.section), str(row.risk), str(row.comments)]);
    }
  }
  if (rkRows.length === 1) rkRows.push(["(no risks)", "", "", ""]);
  XLSX.utils.book_append_sheet(wb, XLSX.utils.aoa_to_sheet(rkRows), sheetName("Risks"));

  XLSX.writeFile(wb, `${fileStem}-well_plan.xlsx`);
}
