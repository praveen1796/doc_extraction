import { useState, Component } from "react";
import type { ReactNode, ErrorInfo } from "react";
import { useNavigate } from "react-router-dom";
import { motion, AnimatePresence } from "framer-motion";
import {
  ChevronDown, ChevronRight, AlertTriangle,
  Layers, Droplets, Shield, Info, Ruler, Hash,
  Gauge,   Download, Copy, Check, Code, FileSpreadsheet,
} from "lucide-react";
import type { ExtractionResponse } from "@/services/api";
import {
  buildFullExtractionExport,
  copyJsonToClipboard,
  downloadJson,
  downloadExtractionXlsx,
  extractionJsonBaseName,
  extractionXlsxButtonTitle,
} from "@/lib/extractionExport";

// ═══════════════════════════════════════════════════════════════
//  Well Plan Visualization v1.3-hotfix
//  FIX UI-1: ErrorBoundary prevents white screen on malformed data
//  FIX UI-4: Casing OD/ID always in inches (industry standard)
// ═══════════════════════════════════════════════════════════════

// ── Error Boundary ──
class WellPlanErrorBoundary extends Component<
  { children: ReactNode },
  { hasError: boolean; error: string }
> {
  constructor(props: { children: ReactNode }) {
    super(props);
    this.state = { hasError: false, error: "" };
  }

  static getDerivedStateFromError(error: Error) {
    return { hasError: true, error: error.message };
  }

  componentDidCatch(error: Error, errorInfo: ErrorInfo) {
    console.error("WellPlanView render error:", error, errorInfo);
  }

  render() {
    if (this.state.hasError) {
      return (
        <div className="flex flex-col items-center justify-center h-full p-6 text-center">
          <AlertTriangle className="w-8 h-8 text-amber-500 mb-3" />
          <p className="text-sm font-medium text-foreground mb-1">Well plan data could not be rendered</p>
          <p className="text-xs text-muted-foreground max-w-md">
            The extracted data may be incomplete or malformed. This can happen when the AI model
            runs out of output tokens or the document format is unusual.
          </p>
          <p className="text-xs text-muted-foreground mt-2 font-mono bg-muted/30 px-3 py-1 rounded">
            {this.state.error}
          </p>
        </div>
      );
    }
    return this.props.children;
  }
}

// ── Types matching schema v1.3 ──
interface Well {
  well_name: string;
  well_type: string;
  api_number: string | null;
  afe_number: string | null;
  operator_well_id: string | null;
  permit_id: string | null;
  total_depth_md: number | null;
  total_depth_tvd: number | null;
  lateral_length: string | null;
  target_formation: string;
  design: string;
  surface_coordinates: string;
  coordinate_system: string;
  ground_level: number | null;
  rkb: number | null;
  skid_order: number | null;
  casing_program: CasingSection[];
  formation_tops: FormationTop[];
  drilling_sections: DrillingSection[];
  drilling_fluids: DrillingFluid[];
  risks_and_hazards: Risk[];
  notes: string;
}
interface CasingSection {
  section_name: string; hole_size: number | null; casing_od: number | null;
  casing_id: number | null; drift: number | null; grade: string;
  weight_per_length: number | null; connection: string;
  start_md: number | null; end_md: number | null;
  cement_type: string | null; cement_details: string | null;
}
interface FormationTop { formation_name: string; md: number | null; tvd: number | null; }
interface DrillingSection {
  section_name: string; hole_size: number | null;
  depth_from: number | null; depth_to: number | null; interval: string;
  wob: string; rpm: string; flow_rate: string; rop: string;
  diffp_max: number | null; bha_type: string;
  primary_bit: string; backup_bit: string; comments: string;
}
interface DrillingFluid {
  section: string; fluid_type: string; design_mw: number | null;
  min_mw: number | null; max_mw: number | null; min_fit: number | null;
  mudloggers: string | null; comments: string;
}
interface Risk { section: string; risk: string; comments: string; }
interface Approval { name: string; action: string; datetime: string; }
interface WellPlanData {
  rig_name: string; operator: string; pad_name: string; location: string;
  report_status: string; report_date: string; language: string;
  depth_unit: string; document_format: string;
  approvals: Approval[]; wells: Well[];
}

type DetailTab = "formations" | "casing" | "parameters" | "fluids" | "risks";

// ── Helpers ──
function DR({ label, value, mono }: { label: string; value: string | number | null | undefined; mono?: boolean }) {
  if (value === null || value === undefined || value === "") return null;
  return (
    <div className="flex items-baseline justify-between gap-3 py-2 border-b border-border/20 last:border-0">
      <span className="text-xs text-muted-foreground shrink-0 max-w-[45%] leading-snug">{label}</span>
      <span className={`text-sm font-medium text-foreground text-right min-w-0 break-words ${mono ? "font-mono text-[13px]" : ""}`}>
        {typeof value === "number" ? value.toLocaleString() : value}
      </span>
    </div>
  );
}

function Depth({ value, unit }: { value: number | null; unit: string }) {
  if (value === null || value === undefined) return <span className="text-muted-foreground/50">—</span>;
  return <span className="font-mono text-[11px]">{value.toLocaleString()} {unit}</span>;
}

function Badge({ text, variant = "default" }: { text: string; variant?: "default" | "success" | "warning" | "info" }) {
  const cls = {
    default: "bg-muted text-muted-foreground",
    success: "bg-emerald-500/10 text-emerald-600 dark:text-emerald-400",
    warning: "bg-amber-500/10 text-amber-600 dark:text-amber-400",
    info: "bg-cyan-500/10 text-cyan-600 dark:text-cyan-400",
  }[variant];
  return <span className={`inline-flex items-center px-2 py-0.5 rounded text-[10px] font-medium ${cls}`}>{text}</span>;
}

function WellTypeBadge({ type }: { type: string }) {
  const variant = type === "horizontal" ? "info" : type === "vertical" ? "default" : "warning";
  return <Badge text={type} variant={variant} />;
}

function Section({ title, icon, children, defaultOpen = false }: {
  title: string; icon: React.ReactNode; children: React.ReactNode; defaultOpen?: boolean;
}) {
  const [open, setOpen] = useState(defaultOpen);
  return (
    <div className="border border-border/25 rounded-xl overflow-hidden mb-3 bg-card/30">
      <button type="button" onClick={() => setOpen(!open)}
        className="w-full flex items-center gap-2.5 px-4 py-2.5 text-sm font-semibold text-foreground hover:bg-muted/40 transition-colors">
        {icon}
        <span className="flex-1 text-left">{title}</span>
        {open ? <ChevronDown className="w-4 h-4 text-muted-foreground" /> : <ChevronRight className="w-4 h-4 text-muted-foreground" />}
      </button>
      <AnimatePresence>
        {open && (
          <motion.div initial={{ height: 0, opacity: 0 }} animate={{ height: "auto", opacity: 1 }}
            exit={{ height: 0, opacity: 0 }} transition={{ duration: 0.2 }}>
            <div className="px-4 pb-4 pt-1 border-t border-border/15">{children}</div>
          </motion.div>
        )}
      </AnimatePresence>
    </div>
  );
}

// ── Main Component (exported with ErrorBoundary wrapper) ──
export function WellPlanView({
  rawData,
  fileName = "document.pdf",
  extractionResult,
}: {
  rawData: unknown;
  fileName?: string;
  extractionResult?: ExtractionResponse | null;
}) {
  return (
    <WellPlanErrorBoundary>
      <WellPlanViewInner rawData={rawData} fileName={fileName} extractionResult={extractionResult} />
    </WellPlanErrorBoundary>
  );
}

function WellTwinNavButton() {
  const navigate = useNavigate();
  return (
    <button
      type="button"
      title="Open visual well twin (trajectory, casing, risks)"
      onClick={() => navigate("/well-twin")}
      className="flex items-center gap-1 px-2 py-1 rounded-md text-[10px] font-medium text-accent hover:bg-accent/10 border border-accent/25 hover:border-accent/45 transition-colors"
    >
      <Layers className="h-3.5 w-3.5 shrink-0" aria-hidden />
      <span className="whitespace-nowrap">Well twin</span>
    </button>
  );
}

function WellPlanViewInner({
  rawData,
  fileName,
  extractionResult,
}: {
  rawData: unknown;
  fileName: string;
  extractionResult?: ExtractionResponse | null;
}) {
  const data = rawData as WellPlanData | undefined;
  const [activeWellIdx, setActiveWellIdx] = useState(0);
  const [activeTab, setActiveTab] = useState<DetailTab>("formations");
  const [showRawJson, setShowRawJson] = useState(false);
  const [copied, setCopied] = useState(false);

  const exportPayload = extractionResult
    ? buildFullExtractionExport(extractionResult)
    : (rawData as Record<string, unknown> | undefined);

  const handleDownload = () => {
    if (!exportPayload) return;
    const stem = extractionJsonBaseName(fileName);
    downloadJson(`${stem}-well_plan-extraction`, exportPayload);
  };

  const handleDownloadXlsx = () => {
    if (!extractionResult?.data) return;
    downloadExtractionXlsx(extractionResult, fileName);
  };

  const handleCopy = async () => {
    if (!exportPayload) return;
    try {
      await copyJsonToClipboard(exportPayload);
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    } catch {
      /* ignore */
    }
  };

  if (!data || !data.wells?.length) {
    return (
      <div className="flex flex-col h-full">
        <div className="flex items-center justify-end gap-1.5 px-3 py-2 border-b border-border/20 shrink-0 flex-wrap">
          <WellTwinNavButton />
          <button type="button" title={extractionXlsxButtonTitle(extractionResult?.document_type)} onClick={handleDownloadXlsx} disabled={!extractionResult?.data}
            className="p-1.5 rounded-md text-muted-foreground hover:bg-secondary/60 disabled:opacity-40">
            <FileSpreadsheet className="h-3.5 w-3.5" />
          </button>
          <button type="button" title="Download JSON" onClick={handleDownload} disabled={!exportPayload}
            className="p-1.5 rounded-md text-muted-foreground hover:bg-secondary/60 disabled:opacity-40">
            <Download className="h-3.5 w-3.5" />
          </button>
          <button type="button" title="Copy JSON" onClick={handleCopy} disabled={!exportPayload}
            className="p-1.5 rounded-md text-muted-foreground hover:bg-secondary/60 disabled:opacity-40">
            {copied ? <Check className="h-3.5 w-3.5 text-emerald-500" /> : <Copy className="h-3.5 w-3.5" />}
          </button>
        </div>
        <div className="flex-1 flex flex-col items-center justify-center p-6 text-center text-muted-foreground">
          <p className="text-sm font-medium text-foreground mb-1">No well plan data in this response</p>
          <p className="text-xs max-w-md leading-relaxed">
            The API may have returned an empty <code className="text-[10px] bg-muted px-1 rounded">wells</code> array,
            or the document was classified differently. Use <strong>Download JSON</strong> (above) to inspect the raw payload.
            Ensure the backend is running, demo mode is off, and <code className="text-[10px] bg-muted px-1 rounded">documentType</code> is{" "}
            <code className="text-[10px] bg-muted px-1 rounded">well_plan</code>.
          </p>
        </div>
      </div>
    );
  }

  const wells = data.wells;
  // FIX UI-1: Defensive index bounds check
  const safeIdx = Math.min(activeWellIdx, wells.length - 1);
  const well = wells[safeIdx];
  const unit = data.depth_unit || "ft";
  // FIX UI-4: Casing OD/ID is ALWAYS in inches, even in metric countries.
  // YPF says "cañería 7" (7 inches). Only depths change with depth_unit.
  const sizeUnit = "in";
  const weightUnit = unit === "m" ? "kg/m" : "lbm/ft";

  const tabs: { key: DetailTab; label: string; icon: React.ReactNode; count: number }[] = [
    { key: "formations", label: "Formations", icon: <Layers className="w-3 h-3" />, count: well?.formation_tops?.length ?? 0 },
    { key: "casing", label: "Casing", icon: <Ruler className="w-3 h-3" />, count: well?.casing_program?.length ?? 0 },
    { key: "parameters", label: "Parameters", icon: <Gauge className="w-3 h-3" />, count: well?.drilling_sections?.length ?? 0 },
    { key: "fluids", label: "Fluids", icon: <Droplets className="w-3 h-3" />, count: well?.drilling_fluids?.length ?? 0 },
    { key: "risks", label: "Risks", icon: <Shield className="w-3 h-3" />, count: well?.risks_and_hazards?.length ?? 0 },
  ];

  return (
    <div className="flex flex-col h-full overflow-hidden">
      {/* ── Toolbar: JSON export (full API shape when available) ── */}
      <div className="flex items-center justify-between px-3 py-2 border-b border-border/25 bg-muted/15 shrink-0 gap-2">
        <span className="text-[11px] text-muted-foreground truncate min-w-0">
          Well plan · depths in <strong className="text-foreground">{unit}</strong>
          {data.report_date ? <span className="ml-2 opacity-80">· Report {data.report_date}</span> : null}
        </span>
        <div className="flex items-center gap-1 shrink-0 flex-wrap justify-end">
          <WellTwinNavButton />
          <button type="button" title={extractionXlsxButtonTitle(extractionResult?.document_type)} onClick={handleDownloadXlsx} disabled={!extractionResult?.data}
            className="p-1.5 rounded-md text-muted-foreground hover:text-foreground hover:bg-secondary/60 transition-colors disabled:opacity-40">
            <FileSpreadsheet className="h-3.5 w-3.5" />
          </button>
          <button type="button" title="Download extraction JSON" onClick={handleDownload}
            className="p-1.5 rounded-md text-muted-foreground hover:text-foreground hover:bg-secondary/60 transition-colors">
            <Download className="h-3.5 w-3.5" />
          </button>
          <button type="button" title="Copy JSON" onClick={handleCopy}
            className="p-1.5 rounded-md text-muted-foreground hover:text-foreground hover:bg-secondary/60 transition-colors">
            {copied ? <Check className="h-3.5 w-3.5 text-emerald-500" /> : <Copy className="h-3.5 w-3.5" />}
          </button>
          <button type="button" title="Toggle raw JSON" onClick={() => setShowRawJson((v) => !v)}
            className={`p-1.5 rounded-md transition-colors ${showRawJson ? "bg-primary/15 text-primary" : "text-muted-foreground hover:bg-secondary/60"}`}>
            <Code className="h-3.5 w-3.5" />
          </button>
        </div>
      </div>

      <AnimatePresence>
        {showRawJson && exportPayload && (
          <motion.div initial={{ height: 0, opacity: 0 }} animate={{ height: "auto", opacity: 1 }} exit={{ height: 0, opacity: 0 }}
            className="border-b border-border/25 overflow-hidden shrink-0">
            <pre className="p-3 mx-3 my-2 rounded-lg bg-secondary/40 border border-border/40 text-[11px] font-mono text-foreground/90 max-h-[220px] overflow-auto custom-scrollbar">
              {JSON.stringify(exportPayload, null, 2)}
            </pre>
          </motion.div>
        )}
      </AnimatePresence>

      {/* ── Pad Header ── */}
      <div className="px-4 py-3.5 border-b border-border/20 bg-gradient-to-b from-muted/25 to-transparent shrink-0">
        <div className="flex items-start justify-between gap-2 mb-2">
          <div className="min-w-0">
            <h2 className="text-base font-semibold text-foreground leading-tight truncate">{data.rig_name || "Unknown Rig"}</h2>
            <p className="text-xs text-muted-foreground mt-1 line-clamp-2">
              {data.operator}
              {data.pad_name ? <span className="text-border"> · </span> : null}
              {data.pad_name}
              {data.location ? <span className="text-border"> · </span> : null}
              {data.location}
            </p>
          </div>
          <div className="flex flex-wrap gap-1 justify-end shrink-0">
            {data.language && data.language !== "en" && <Badge text={data.language.toUpperCase()} variant="info" />}
            <Badge text={unit === "m" ? "Metric (m)" : "Imperial (ft)"} variant="default" />
            <Badge text={data.report_status || "Draft"} variant={data.report_status?.toLowerCase().includes("approv") ? "success" : "warning"} />
          </div>
        </div>
        {data.document_format && (
          <Badge text={data.document_format.replace(/_/g, " ")} variant="info" />
        )}
      </div>

      {/* ── Well Selector ── */}
      {wells.length > 1 && (
        <div className="flex gap-1 px-3 py-2 border-b border-border/10 overflow-x-auto shrink-0">
          {wells.map((w, i) => (
            <button key={i} onClick={() => { setActiveWellIdx(i); setActiveTab("formations"); }}
              className={`shrink-0 px-2.5 py-1 rounded text-[10px] font-medium transition-colors ${
                i === activeWellIdx
                  ? "bg-cyan-500/15 text-cyan-600 dark:text-cyan-400 ring-1 ring-cyan-500/30"
                  : "bg-muted/40 text-muted-foreground hover:bg-muted/70"
              }`}>
              {w.skid_order != null && <span className="mr-1 opacity-60">#{w.skid_order}</span>}
              {(w.well_name ?? `Well ${i + 1}`).split(" ").pop() || `Well ${i + 1}`}
            </button>
          ))}
        </div>
      )}

      {/* ── Well Content ── */}
      <div className="flex-1 overflow-y-auto px-3 py-3 space-y-2">
        <AnimatePresence mode="wait">
          <motion.div key={activeWellIdx} initial={{ opacity: 0, x: 8 }} animate={{ opacity: 1, x: 0 }}
            exit={{ opacity: 0, x: -8 }} transition={{ duration: 0.15 }}>

            {/* Well Identity */}
            <Section title="Well identity" icon={<Hash className="w-3.5 h-3.5 text-cyan-500" />} defaultOpen>
              <div className="grid grid-cols-2 gap-x-6 pt-2">
                <div>
                  <DR label="Well name" value={well?.well_name ?? "Unknown"} />
                  <DR label="Well type" value={well.well_type} />
                  <DR label="API number" value={well.api_number} mono />
                  <DR label="AFE / authorization" value={well.afe_number} mono />
                  <DR label="Operator ID" value={well.operator_well_id} mono />
                  <DR label="Permit ID" value={well.permit_id} mono />
                  <DR label="Skid order" value={well.skid_order} />
                </div>
                <div>
                  <DR label={`Total depth MD (${unit})`} value={well.total_depth_md} mono />
                  <DR label={`Total depth TVD (${unit})`} value={well.total_depth_tvd} mono />
                  <DR label="Lateral length" value={well.lateral_length} />
                  <DR label="Target formation" value={well.target_formation} />
                  <DR label="Design" value={well.design} />
                  <DR label={`Ground level (${unit})`} value={well.ground_level} mono />
                  <DR label={`RKB (${unit})`} value={well.rkb} mono />
                </div>
              </div>
              {well.surface_coordinates && (
                <div className="mt-2 pt-2 border-t border-border/10">
                  <DR label="Surface coordinates" value={well.surface_coordinates} mono />
                  <DR label="Coordinate system" value={well.coordinate_system} />
                </div>
              )}
            </Section>

            {/* Tab Bar */}
            <div className="flex flex-wrap gap-1 mb-3 mt-2">
              {tabs.map(t => (
                <button key={t.key} type="button" onClick={() => setActiveTab(t.key)}
                  className={`flex items-center gap-1.5 px-2.5 py-1.5 rounded-lg text-xs font-medium transition-colors ${
                    activeTab === t.key
                      ? "bg-cyan-500/15 text-cyan-700 dark:text-cyan-300 ring-1 ring-cyan-500/25"
                      : "text-muted-foreground hover:bg-muted/50"
                  }`}>
                  {t.icon} {t.label}
                  {t.count > 0 && <span className="tabular-nums opacity-70">({t.count})</span>}
                </button>
              ))}
            </div>

            {/* Tab Content */}
            <AnimatePresence mode="wait">
              <motion.div key={activeTab} initial={{ opacity: 0, y: 4 }} animate={{ opacity: 1, y: 0 }}
                transition={{ duration: 0.15 }}>

                {activeTab === "formations" && (
                  <div className="border border-border/25 rounded-xl overflow-hidden">
                    {well.formation_tops?.length > 0 ? (
                      <div className="overflow-x-auto">
                      <table className="w-full min-w-[320px] text-sm">
                        <thead>
                          <tr className="bg-muted/40 border-b border-border/20">
                            <th className="text-left px-3 py-2.5 font-semibold text-xs text-muted-foreground uppercase tracking-wide">Formation</th>
                            <th className="text-right px-3 py-2.5 font-semibold text-xs text-muted-foreground uppercase tracking-wide">MD ({unit})</th>
                            <th className="text-right px-3 py-2.5 font-semibold text-xs text-muted-foreground uppercase tracking-wide">TVD ({unit})</th>
                          </tr>
                        </thead>
                        <tbody>
                          {well.formation_tops.map((f, i) => (
                            <tr key={i} className={`border-t border-border/15 hover:bg-muted/20 ${i % 2 === 1 ? "bg-muted/10" : ""}`}>
                              <td className="px-3 py-2 font-medium text-foreground">{f.formation_name}</td>
                              <td className="px-3 py-2 text-right font-mono text-[13px]"><Depth value={f.md} unit="" /></td>
                              <td className="px-3 py-2 text-right font-mono text-[13px]"><Depth value={f.tvd} unit="" /></td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                      </div>
                    ) : (
                      <div className="p-4 text-center text-muted-foreground text-[11px]">No formation data in document</div>
                    )}
                  </div>
                )}

                {activeTab === "casing" && (
                  <div className="space-y-2">
                    {well.casing_program?.length > 0 ? well.casing_program.map((c, i) => (
                      <div key={i} className="border border-border/20 rounded-lg p-3">
                        <div className="flex items-center justify-between mb-2">
                          <span className="text-[12px] font-medium">{c.section_name || `Section ${i + 1}`}</span>
                          {c.hole_size && <Badge text={`${c.hole_size}" hole`} />}
                        </div>
                        <div className="grid grid-cols-2 gap-x-4">
                          <DR label={`Casing OD (${sizeUnit})`} value={c.casing_od} mono />
                          <DR label={`Casing ID (${sizeUnit})`} value={c.casing_id} mono />
                          <DR label={`Drift (${sizeUnit})`} value={c.drift} mono />
                          <DR label="Grade" value={c.grade} />
                          <DR label={`Weight (${weightUnit})`} value={c.weight_per_length} mono />
                          <DR label="Connection" value={c.connection} />
                          <DR label={`Start MD (${unit})`} value={c.start_md} mono />
                          <DR label={`End MD (${unit})`} value={c.end_md} mono />
                          <DR label="Cement type" value={c.cement_type} />
                          <DR label="Cement details" value={c.cement_details} />
                        </div>
                      </div>
                    )) : (
                      <div className="p-4 text-center text-muted-foreground text-[11px] border border-border/20 rounded-lg">No casing data in document</div>
                    )}
                  </div>
                )}

                {activeTab === "parameters" && (
                  <div className="border border-border/25 rounded-xl overflow-hidden">
                    {well.drilling_sections?.length > 0 ? (
                      <div className="overflow-x-auto">
                      <table className="w-full min-w-[640px] text-xs">
                        <thead>
                          <tr className="bg-muted/40 border-b border-border/20">
                            <th className="text-left px-2 py-2 font-semibold text-[10px] uppercase tracking-wide text-muted-foreground sticky left-0 bg-muted/40 z-[1]">Section</th>
                            <th className="text-left px-2 py-2 font-semibold text-[10px] uppercase tracking-wide text-muted-foreground">Hole ({sizeUnit})</th>
                            <th className="text-left px-2 py-2 font-semibold text-[10px] uppercase tracking-wide text-muted-foreground">Depth ({unit})</th>
                            <th className="text-left px-2 py-2 font-semibold text-[10px] uppercase tracking-wide text-muted-foreground">WOB</th>
                            <th className="text-left px-2 py-2 font-semibold text-[10px] uppercase tracking-wide text-muted-foreground">RPM</th>
                            <th className="text-left px-2 py-2 font-semibold text-[10px] uppercase tracking-wide text-muted-foreground">Flow</th>
                            <th className="text-left px-2 py-2 font-semibold text-[10px] uppercase tracking-wide text-muted-foreground">ROP</th>
                          </tr>
                        </thead>
                        <tbody>
                          {well.drilling_sections.map((s, i) => (
                            <tr key={i} className={`border-t border-border/15 hover:bg-muted/20 ${i % 2 === 1 ? "bg-muted/10" : ""}`}>
                              <td className="px-2 py-2 font-medium text-sm sticky left-0 bg-background/95 backdrop-blur-sm z-[1]">{s.section_name}</td>
                              <td className="px-2 py-2 font-mono text-[13px]">{s.hole_size ?? "—"}</td>
                              <td className="px-2 py-2 font-mono text-[12px] whitespace-nowrap">
                                {s.depth_from != null && s.depth_to != null ? `${s.depth_from.toLocaleString()}–${s.depth_to.toLocaleString()}` : "—"}
                              </td>
                              <td className="px-2 py-2 max-w-[72px] truncate" title={s.wob}>{s.wob || "—"}</td>
                              <td className="px-2 py-2 max-w-[56px] truncate">{s.rpm || "—"}</td>
                              <td className="px-2 py-2 max-w-[72px] truncate" title={s.flow_rate}>{s.flow_rate || "—"}</td>
                              <td className="px-2 py-2 max-w-[72px] truncate">{s.rop || "—"}</td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                      </div>
                    ) : (
                      <div className="p-4 text-center text-muted-foreground text-[11px]">No drilling parameters in document</div>
                    )}
                  </div>
                )}

                {activeTab === "fluids" && (
                  <div className="border border-border/25 rounded-xl overflow-hidden">
                    {well.drilling_fluids?.length > 0 ? (
                      <div className="overflow-x-auto">
                      <table className="w-full min-w-[520px] text-xs">
                        <thead>
                          <tr className="bg-muted/40 border-b border-border/20">
                            <th className="text-left px-2 py-2 font-semibold text-[10px] uppercase tracking-wide text-muted-foreground">Section</th>
                            <th className="text-left px-2 py-2 font-semibold text-[10px] uppercase tracking-wide text-muted-foreground">Type</th>
                            <th className="text-right px-2 py-2 font-semibold text-[10px] uppercase tracking-wide text-muted-foreground">Design MW</th>
                            <th className="text-right px-2 py-2 font-semibold text-[10px] uppercase tracking-wide text-muted-foreground">Min MW</th>
                            <th className="text-right px-2 py-2 font-semibold text-[10px] uppercase tracking-wide text-muted-foreground">Max MW</th>
                            <th className="text-right px-2 py-2 font-semibold text-[10px] uppercase tracking-wide text-muted-foreground">Min FIT</th>
                            <th className="text-left px-2 py-2 font-semibold text-[10px] uppercase tracking-wide text-muted-foreground">Mudloggers</th>
                          </tr>
                        </thead>
                        <tbody>
                          {well.drilling_fluids.map((f, i) => (
                            <tr key={i} className={`border-t border-border/15 hover:bg-muted/20 ${i % 2 === 1 ? "bg-muted/10" : ""}`}>
                              <td className="px-2 py-2 font-medium text-sm">{f.section}</td>
                              <td className="px-2 py-2">{f.fluid_type}</td>
                              <td className="px-2 py-2 text-right font-mono text-[13px]">{f.design_mw ?? "—"}</td>
                              <td className="px-2 py-2 text-right font-mono text-[13px]">{f.min_mw ?? "—"}</td>
                              <td className="px-2 py-2 text-right font-mono text-[13px]">{f.max_mw ?? "—"}</td>
                              <td className="px-2 py-2 text-right font-mono text-[13px]">{f.min_fit ?? "—"}</td>
                              <td className="px-2 py-2 text-[11px] max-w-[140px] truncate" title={f.mudloggers ?? ""}>{f.mudloggers ?? "—"}</td>
                            </tr>
                          ))}
                        </tbody>
                      </table>
                      </div>
                    ) : (
                      <div className="p-4 text-center text-muted-foreground text-[11px]">No drilling fluid data in document</div>
                    )}
                  </div>
                )}

                {activeTab === "risks" && (
                  <div className="space-y-2">
                    {well.risks_and_hazards?.length > 0 ? well.risks_and_hazards.map((r, i) => (
                      <div key={i} className="border border-amber-500/20 bg-amber-500/5 rounded-lg p-3">
                        <div className="flex items-center gap-2 mb-1">
                          <AlertTriangle className="w-3 h-3 text-amber-500" />
                          <span className="text-[11px] font-medium text-amber-600 dark:text-amber-400">{r.section}</span>
                        </div>
                        <p className="text-[11px] font-medium text-foreground">{r.risk}</p>
                        {r.comments && <p className="text-[10px] text-muted-foreground mt-1">{r.comments}</p>}
                      </div>
                    )) : (
                      <div className="p-4 text-center text-muted-foreground text-[11px] border border-border/20 rounded-lg">No risk data in document</div>
                    )}
                  </div>
                )}

              </motion.div>
            </AnimatePresence>

            {/* Notes */}
            {well.notes && (
              <Section title="Notes" icon={<Info className="w-3.5 h-3.5 text-muted-foreground" />}>
                <p className="text-sm text-foreground/85 leading-relaxed pt-2 whitespace-pre-line">{well.notes}</p>
              </Section>
            )}

          </motion.div>
        </AnimatePresence>
      </div>

      {/* ── Footer ── */}
      <div className="px-4 py-2 border-t border-border/20 flex justify-between items-center text-[10px] text-muted-foreground shrink-0">
        <span>{wells.length} well{wells.length !== 1 ? "s" : ""} on pad</span>
        <span>Well {safeIdx + 1} of {wells.length}</span>
      </div>
    </div>
  );
}
