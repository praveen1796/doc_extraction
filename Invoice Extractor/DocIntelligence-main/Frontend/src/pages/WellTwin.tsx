import { useMemo, useEffect, useRef, useState } from "react";
import { useNavigate } from "react-router-dom";
import { motion, AnimatePresence } from "framer-motion";
import {
  ChevronLeft, Target, Layers, Ruler, Gauge, Droplets, Wrench,
  Shield, AlertTriangle, CheckCircle2, Users, FileText, MapPin,
  Activity, Send, Sparkles, Eye, EyeOff, Crosshair, Zap,
} from "lucide-react";
import { useExtractionResult } from "@/hooks/useExtractionResult";
import { extractDocument, extractDocumentDemo, isDemoMode } from "@/services/api";
import { DigitalWellTwin } from "@/components/workspace/wellplan/DigitalWellTwin";

// ═══════════════════════════════════════════════════════════════
//  WELL PLAN INTELLIGENCE STUDIO
//  "Palantir + F1 Telemetry + Premium Subsurface Intelligence"
// ═══════════════════════════════════════════════════════════════

interface Well { [key: string]: any; }
interface WellPlanData { [key: string]: any; }

type BottomTab = "overview" | "casing" | "bha" | "envelope" | "hazards" | "offsets" | "params";

export default function WellTwin() {
  const navigate = useNavigate();
  const { file, documentType, result, fileName, setResult, isSample } = useExtractionResult();
  const extractionTriggered = useRef(false);
  const [extractionError, setExtractionError] = useState<string | null>(null);
  const [activeWellIdx, setActiveWellIdx] = useState(0);
  const [waitProgress, setWaitProgress] = useState(0);
  const [bottomTab, setBottomTab] = useState<BottomTab>("overview");
  const [aiQuery, setAiQuery] = useState("");
  const [riskOverlay, setRiskOverlay] = useState(false);
  const [hlSection, setHlSection] = useState<string | null>(null);

  // ── Extraction ──
  useEffect(() => {
    if (result || extractionTriggered.current) return;
    extractionTriggered.current = true;
    const f2 = file ?? new File([new Blob(["demo"])], "Demo_Well_Plan.pdf", { type: "application/pdf" });
    if (isDemoMode() || !file || isSample) { extractDocumentDemo(f2, undefined, "well_plan").then(r => setResult(r, f2.name)); return; }
    extractDocument(file, { documentType }).then(r => setResult(r, file.name))
      .catch(err => { setExtractionError(err instanceof Error ? err.message : String(err)); extractDocumentDemo(file, undefined, "well_plan").then(r => setResult(r, file.name)); });
  }, [file, result, documentType, setResult, isSample]);

  useEffect(() => { if (result) return; const t = setInterval(() => setWaitProgress(p => Math.min(p + 0.5, 58)), 500); return () => clearInterval(t); }, [result]);

  const rawData = useMemo(() => result?.data ? result.data as unknown as WellPlanData : null, [result]);
  const wells: Well[] = rawData?.wells ?? [];
  const w = wells[activeWellIdx];

  // ═══ WAITING ═══
  if (!rawData) return (
    <div className="min-h-screen bg-background flex flex-col items-center justify-center">
      <motion.div animate={{ scale: [1, 1.04, 1], boxShadow: ["0 0 0 0 hsl(160 70% 45% / 0)", "0 0 60px 16px hsl(160 70% 45% / 0.15)", "0 0 0 0 hsl(160 70% 45% / 0)"] }}
        transition={{ duration: 3, repeat: Infinity }} className="w-28 h-28 rounded-2xl flex items-center justify-center mb-8"
        style={{ background: "hsl(var(--accent) / 0.08)", border: "1px solid hsl(var(--accent) / 0.2)" }}>
        <Target className="h-12 w-12 text-accent" />
      </motion.div>
      <p className="text-base font-bold text-foreground mb-2">Building Well Intelligence…</p>
      <p className="text-xs text-muted-foreground mb-6">{fileName || "Processing"}</p>
      <div className="w-80 h-1.5 rounded-full bg-secondary/30 overflow-hidden">
        <motion.div className="h-full rounded-full" style={{ width: `${waitProgress}%`, background: "linear-gradient(90deg, hsl(var(--accent)), hsl(var(--primary)))", boxShadow: "0 0 12px hsl(var(--accent) / 0.4)" }} />
      </div>
    </div>
  );

  // ═══ MAIN LAYOUT ═══
  return (
    <div
      className="h-screen flex flex-col overflow-hidden text-slate-200 antialiased"
      style={{
        background: "linear-gradient(165deg, #0b1120 0%, #0f172a 42%, #0c1322 100%)",
        color: "rgb(226 232 240)",
      }}
    >

      {/* ═══ 1. MISSION SUMMARY HERO STRIP ═══ */}
      {w && <MissionStrip data={rawData!} well={w} wells={wells} activeIdx={activeWellIdx} setActiveIdx={(i) => { setActiveWellIdx(i); setBottomTab("overview"); }} onBack={() => navigate("/workspace")} error={extractionError} onDismissError={() => setExtractionError(null)} />}

      {/* ═══ 2. MAIN CONTENT: Trajectory + AI Brief ═══ */}
      {w && (
        <div className="flex-1 flex min-h-0 overflow-hidden">
          {/* LEFT: Trajectory Canvas (62%) */}
          <div className="flex-[62] flex flex-col min-w-0 border-r border-slate-700/40 bg-slate-950/30">
            {/* Toolbar */}
            <div className="flex items-center gap-2 px-3 py-2 border-b border-slate-700/50 flex-shrink-0 bg-slate-900/40">
              <button
                type="button"
                onClick={() => setRiskOverlay(!riskOverlay)}
                className={`flex items-center gap-2 px-3 py-1.5 rounded-lg text-xs font-medium transition-all border ${
                  riskOverlay
                    ? "bg-red-500/15 text-red-300 border-red-500/35 shadow-[0_0_12px_-4px_rgba(239,68,68,0.4)]"
                    : "text-slate-300 hover:text-slate-100 border-slate-600/60 bg-slate-800/50 hover:bg-slate-800"
                }`}
              >
                <AlertTriangle className="h-3.5 w-3.5 shrink-0" />
                {riskOverlay ? "Risks overlay on" : "Show risk overlay"}
              </button>
              <span className="text-[11px] text-slate-500 ml-auto hidden sm:inline max-w-[min(280px,40vw)] truncate">
                Hover formations for TVD · drag vertically for depth readout
              </span>
            </div>
            <div className="flex-1 overflow-hidden relative">
              <AnimatePresence mode="wait">
                <motion.div key={activeWellIdx} initial={{ opacity: 0 }} animate={{ opacity: 1 }} exit={{ opacity: 0 }} className="h-full">
                  <DigitalWellTwin wellName={w.well_name} totalDepthMD={(w.total_depth_md ?? w.total_depth_md_ft) ?? 0} totalDepthTVD={(w.total_depth_tvd ?? w.total_depth_tvd_ft) ?? 0}
                    formationTops={w.formation_tops} casingProgram={w.casing_program} drillingFluids={w.drilling_fluids} lateralLength={w.lateral_length}
                    riskOverlay={riskOverlay} risks={w.risks_and_hazards} highlightSection={hlSection} />
                </motion.div>
              </AnimatePresence>
            </div>
          </div>

          {/* RIGHT: Engineering brief (readable rail) */}
          <div className="flex-[38] flex flex-col min-w-0 overflow-hidden bg-slate-950/80 border-l border-slate-700/40 shadow-[-12px_0_40px_-20px_rgba(0,0,0,0.5)]">
            <div className="px-4 py-3 border-b border-slate-700/50 flex items-center gap-3 flex-shrink-0 bg-slate-900/50">
              <div className="w-9 h-9 rounded-xl flex items-center justify-center bg-gradient-to-br from-violet-500/20 to-cyan-500/20 border border-slate-600/50">
                <Sparkles className="h-4 w-4 text-violet-300" />
              </div>
              <div className="min-w-0">
                <h3 className="text-sm font-semibold text-slate-100 tracking-tight">Engineering brief</h3>
                <p className="text-[11px] text-slate-500 mt-0.5">Structured readout from extraction</p>
              </div>
            </div>

            <div className="flex-1 overflow-y-auto custom-scrollbar px-4 py-4 space-y-5 min-h-0">
              {/* A. Critical Decisions */}
              <CriticalDecisions well={w} />
              {/* B. Risk Radar */}
              <RiskRadar well={w} />
              {/* C. Section Cards */}
              <SectionCards well={w} onHoverSection={setHlSection} />
              {/* D. Recommended Actions */}
              <RecommendedActions well={w} />
            </div>

            {/* Prompt strip (placeholder — wire to chat API when ready) */}
            <div className="px-4 py-3 border-t border-slate-700/50 flex-shrink-0 bg-slate-900/60">
              <div className="flex gap-2 mb-2 overflow-x-auto custom-scrollbar pb-0.5">
                {["Show lateral risks", "Why OBM here?", "Compare casing", "Mud weight window?"].map((p, i) => (
                  <button
                    key={i}
                    type="button"
                    onClick={() => setAiQuery(p)}
                    className="flex-shrink-0 px-2.5 py-1.5 rounded-lg text-xs font-medium border border-slate-600/70 bg-slate-800/80 text-slate-300 hover:text-slate-100 hover:border-cyan-500/40 hover:bg-slate-800 transition-colors"
                  >
                    {p}
                  </button>
                ))}
              </div>
              <div className="flex gap-2">
                <input
                  value={aiQuery}
                  onChange={(e) => setAiQuery(e.target.value)}
                  placeholder="Ask about this well plan…"
                  className="flex-1 text-sm px-3 py-2.5 rounded-xl bg-slate-900 border border-slate-600/80 text-slate-100 placeholder:text-slate-500 focus:outline-none focus:ring-2 focus:ring-cyan-500/30 focus:border-cyan-500/50"
                />
                <button
                  type="button"
                  className="p-2.5 rounded-xl bg-gradient-to-br from-violet-600/80 to-cyan-600/70 border border-slate-500/50 text-white hover:opacity-95 transition-opacity"
                  title="Send (demo — not connected)"
                >
                  <Send className="h-4 w-4" />
                </button>
              </div>
            </div>
          </div>
        </div>
      )}

      {/* ═══ 3. BOTTOM WORKSPACE TABS ═══ */}
      {w && <BottomWorkspace well={w} wells={wells} data={rawData!} tab={bottomTab} setTab={setBottomTab} />}
    </div>
  );
}

// ═══════════════════════════════════════════════════════════════
//  MISSION SUMMARY HERO STRIP
// ═══════════════════════════════════════════════════════════════
function MissionStrip({ data, well, wells, activeIdx, setActiveIdx, onBack, error, onDismissError }: any) {
  return (
    <div className="flex-shrink-0 border-b border-slate-700/50 bg-slate-900/40 backdrop-blur-sm">
      {/* Navigation + Well tabs */}
      <div className="flex items-center px-4 py-3 gap-3">
        <button
          type="button"
          onClick={onBack}
          className="p-2 rounded-lg hover:bg-slate-800 text-slate-400 hover:text-slate-100 border border-transparent hover:border-slate-600 transition-colors shrink-0"
          title="Back to workspace"
        >
          <ChevronLeft className="h-5 w-5" />
        </button>
        <div className="flex-1 min-w-0">
          <div className="flex items-center gap-2 flex-wrap">
            <h1 className="text-base sm:text-lg font-semibold text-slate-50 truncate max-w-[min(100%,52rem)] tracking-tight">
              {data.rig_name} — {well.well_name}
            </h1>
            <span className="inline-flex items-center gap-1.5 text-xs font-medium px-2.5 py-1 rounded-full bg-emerald-500/10 text-emerald-300 border border-emerald-500/25">
              <CheckCircle2 className="h-3.5 w-3.5 shrink-0" />
              {data.report_status}
            </span>
          </div>
          <p className="text-xs text-slate-400 mt-1 leading-snug">
            {[data.operator, data.pad_name, data.report_date].filter(Boolean).join(" · ")}
          </p>
        </div>
        <div className="flex gap-1.5 ml-2 overflow-x-auto max-w-[40vw] sm:max-w-none pb-0.5 shrink-0">
          {wells.map((wl: any, i: number) => {
            const short = wl.well_name.split(" ").pop();
            return (
              <button
                key={i}
                type="button"
                onClick={() => setActiveIdx(i)}
                className={`px-3 py-2 rounded-lg text-xs font-medium transition-all border whitespace-nowrap ${
                  i === activeIdx
                    ? "text-slate-50 bg-slate-800 border-cyan-500/40 shadow-[0_0_0_1px_rgba(34,211,238,0.15)]"
                    : "text-slate-400 border-slate-700/60 bg-slate-900/50 hover:text-slate-200 hover:border-slate-600"
                }`}
              >
                {wl.skid_order != null && (
                  <span className="inline-flex items-center justify-center min-w-[1.25rem] h-5 px-1 rounded text-[10px] font-bold mr-1.5 bg-slate-700/80 text-slate-200">
                    {wl.skid_order}
                  </span>
                )}
                {short}
              </button>
            );
          })}
        </div>
      </div>

      {error && (
        <div className="mx-4 mb-2 px-3 py-2 rounded-lg bg-amber-500/10 border border-amber-500/25 text-xs text-amber-200 flex items-center gap-2">
          <span className="w-2 h-2 rounded-full bg-amber-400 shrink-0" />
          <span className="min-w-0">{error}</span>
          <button type="button" onClick={onDismissError} className="ml-auto text-amber-400/70 hover:text-amber-200 px-1">
            ✕
          </button>
        </div>
      )}

      {/* Hero metrics band */}
      <div className="px-4 py-3 flex items-center gap-x-6 gap-y-2 border-t border-slate-800/80 overflow-x-auto bg-slate-950/40">
        {[
          { l: "MD", v: (well.total_depth_md ?? well.total_depth_md_ft)?.toLocaleString(), u: "ft", c: "#22d3ee" },
          { l: "TVD", v: (well.total_depth_tvd ?? well.total_depth_tvd_ft)?.toLocaleString(), u: "ft", c: "#34d399" },
          { l: "Lateral", v: well.lateral_length, u: "", c: "#fbbf24" },
          { l: "Target", v: well.target_formation, u: "", c: "#c4b5fd" },
          { l: "Formations", v: well.formation_tops?.length, u: "", c: "#38bdf8" },
          { l: "Casing", v: well.casing_program?.length, u: "strings", c: "#4ade80" },
          { l: "Risks", v: well.risks_and_hazards?.length, u: "", c: "#fb7185" },
          { l: "API", v: well.api_number, u: "", c: "#cbd5e1" },
          { l: "AFE", v: well.afe_number, u: "", c: "#cbd5e1" },
        ].map((m, i) => (
          <div key={i} className="flex flex-col gap-0.5 flex-shrink-0 min-w-[3.5rem]">
            <span className="text-[10px] font-semibold text-slate-500 uppercase tracking-wider">{m.l}</span>
            <div className="flex items-baseline gap-1 flex-wrap">
              <span className="text-base font-bold font-mono tabular-nums drop-shadow-sm" style={{ color: m.c }}>
                {m.v ?? "—"}
              </span>
              {m.u ? <span className="text-xs text-slate-500">{m.u}</span> : null}
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}

// ═══════════════════════════════════════════════════════════════
//  RIGHT RAIL COMPONENTS
// ═══════════════════════════════════════════════════════════════

function CriticalDecisions({ well }: { well: Well }) {
  const decisions: { icon: string; text: string; severity: "high" | "medium" | "info" }[] = [];
  if (well.target_formation) decisions.push({ icon: "🎯", text: `Target: ${well.target_formation}. Geosteering must be optimized for this zone.`, severity: "info" });
  well.drilling_fluids?.forEach((f: any) => { if (f.comments) decisions.push({ icon: f.section === "Production" ? "🛢️" : "💧", text: `${f.section}: ${f.comments}`, severity: "medium" }); });
  well.risks_and_hazards?.filter((r: any) => r.risk.toLowerCase().includes("water") || r.risk.toLowerCase().includes("gas")).forEach((r: any) => {
    decisions.push({ icon: "⚠️", text: `${r.section} — ${r.risk}: ${r.comments}`, severity: "high" });
  });
  well.anti_collision?.filter((ac: any) => ac.sf < ac.sf_req * 1.3).forEach((ac: any) => {
    decisions.push({ icon: "🔴", text: `TD SF ${ac.sf.toFixed(2)} vs ${ac.ref_well} (req >${ac.sf_req}). Geosteering TVD changes will impact CtC/EtE.`, severity: "high" });
  });
  if (!decisions.length) return null;
  return (
    <section className="rounded-xl border border-slate-700/60 bg-slate-900/50 p-4 shadow-sm">
      <h4 className="text-xs font-bold uppercase tracking-wide text-rose-300 mb-3 flex items-center gap-2">
        <Zap className="h-4 w-4 shrink-0" /> Critical decisions
      </h4>
      <div className="space-y-2.5">
        {decisions.slice(0, 5).map((d, i) => (
          <motion.div
            key={i}
            initial={{ opacity: 0, x: 8 }}
            animate={{ opacity: 1, x: 0 }}
            transition={{ delay: i * 0.06 }}
            className={`px-3.5 py-3 rounded-lg text-sm leading-relaxed border ${
              d.severity === "high"
                ? "bg-rose-950/40 border-rose-500/25 text-slate-100"
                : d.severity === "medium"
                  ? "bg-amber-950/30 border-amber-500/25 text-slate-100"
                  : "bg-cyan-950/25 border-cyan-500/20 text-slate-100"
            }`}
          >
            <span className="mr-2 select-none">{d.icon}</span>
            <span className="text-slate-200">{d.text}</span>
          </motion.div>
        ))}
      </div>
    </section>
  );
}

function RiskRadar({ well }: { well: Well }) {
  const risks = well.risks_and_hazards || [];
  if (!risks.length) return null;
  const getColor = (r: any) => {
    const t = r.risk.toLowerCase();
    if (t.includes("loss")) return "#F59E0B";
    if (t.includes("water")) return "#3B82F6";
    if (t.includes("gas")) return "#EF4444";
    if (t.includes("yield")) return "#8B5CF6";
    return "#64748B";
  };
  return (
    <section className="rounded-xl border border-slate-700/60 bg-slate-900/50 p-4 shadow-sm">
      <h4 className="text-xs font-bold text-amber-200 uppercase tracking-wide mb-3 flex items-center gap-2">
        <AlertTriangle className="h-4 w-4 shrink-0" /> Risk radar
      </h4>
      <div className="flex flex-wrap gap-2">
        {risks.map((r: any, i: number) => (
          <motion.div
            key={i}
            initial={{ scale: 0.98, opacity: 0 }}
            animate={{ scale: 1, opacity: 1 }}
            transition={{ delay: i * 0.04 }}
            className="group relative px-3 py-2 rounded-lg text-xs cursor-default border border-slate-600/70 bg-slate-950/80 max-w-full border-l-[3px]"
            style={{ borderLeftColor: getColor(r) }}
          >
            <span className="mr-1.5 text-amber-300" aria-hidden>
              ⚠️
            </span>
            <span className="text-slate-100 font-semibold">{r.risk}</span>
            <span className="text-slate-400 font-normal ml-1.5">· {r.section}</span>
            <div
              className="absolute bottom-full left-0 mb-2 w-[min(18rem,calc(100vw-4rem))] p-3 rounded-lg text-xs text-slate-200 leading-relaxed opacity-0 group-hover:opacity-100 transition-opacity pointer-events-none z-50 shadow-xl border border-slate-600/80 bg-slate-950"
            >
              {r.comments || "No additional detail."}
            </div>
          </motion.div>
        ))}
        {well.anti_collision?.filter((ac: any) => ac.sf < ac.sf_req * 1.3).map((ac: any, i: number) => (
          <div
            key={`ac-${i}`}
            className="px-3 py-2 rounded-lg text-xs font-medium bg-rose-950/50 border border-rose-500/30 text-rose-100"
          >
            <span className="mr-1" aria-hidden>
              🔴
            </span>
            SF {ac.sf.toFixed(2)} vs {ac.ref_well.split("(")[0].trim()}
          </div>
        ))}
      </div>
    </section>
  );
}

function sectionDepthLabel(s: any): string {
  const from = s.depth_from ?? s.depth_from_ft;
  const to = s.depth_to ?? s.depth_to_ft;
  const hole = s.hole_size ?? s.hole_size_in;
  if (from != null && to != null) return `${hole != null ? `${hole}" · ` : ""}${Number(from).toLocaleString()}→${Number(to).toLocaleString()}'`;
  if (from != null || to != null) return `${[from, to].filter((x) => x != null).map((x) => Number(x).toLocaleString()).join(" → ")}'`;
  return hole != null ? `${hole}"` : "—";
}

function SectionCards({ well, onHoverSection }: { well: Well; onHoverSection?: (s: string | null) => void }) {
  const sections = well.drilling_sections || [];
  const colors: Record<string, string> = { Intermediate: "#fbbf24", Curve: "#c4b5fd", Lateral: "#4ade80", Surface: "#94a3b8" };
  const wob = (s: any) => s.wob_klbf ?? s.wob;
  const flow = (s: any) => s.flow_rate_gpm ?? s.flow_rate;
  const rop = (s: any) => s.rop_fth ?? s.rop;
  const diff = (s: any) => s.diffp_max_psi ?? s.diffp_max;
  return (
    <section className="rounded-xl border border-slate-700/60 bg-slate-900/50 p-4 shadow-sm">
      <h4 className="text-xs font-bold text-cyan-200 uppercase tracking-wide mb-3 flex items-center gap-2">
        <Layers className="h-4 w-4 shrink-0" /> Section plan
      </h4>
      <div className="space-y-3">
        {sections.map((s: any, i: number) => {
          const c = colors[s.section_name as string] || "#94a3b8";
          const fluid = well.drilling_fluids?.find((f: any) =>
            s.section_name === "Intermediate" ? f.section === "Intermediate" : f.section === "Production"
          );
          return (
            <div
              key={i}
              className="rounded-xl overflow-hidden border border-slate-700/70 bg-slate-950/40 transition-shadow hover:shadow-[0_0_0_1px_rgba(34,211,238,0.2)]"
              style={{ boxShadow: `inset 0 0 0 1px ${c}18` }}
              onMouseEnter={() => onHoverSection?.(s.section_name)}
              onMouseLeave={() => onHoverSection?.(null)}
            >
              <div className="px-3 py-2.5 flex items-center justify-between gap-2 flex-wrap" style={{ background: `${c}12` }}>
                <span className="text-sm font-semibold" style={{ color: c }}>
                  {s.section_name}
                </span>
                <span className="text-xs font-mono text-slate-300 tabular-nums">{sectionDepthLabel(s)}</span>
              </div>
              <div className="px-3 py-3 grid grid-cols-2 sm:grid-cols-3 gap-x-4 gap-y-2.5 text-xs">
                {[
                  ["RPM", s.rpm],
                  ["WOB", wob(s)],
                  ["Flow", flow(s)],
                  ["ROP", rop(s)],
                  ["DiffP", diff(s)],
                  fluid ? ["Mud", `${fluid.design_mw ?? "—"} ppg`] : null,
                ]
                  .filter(Boolean)
                  .map(([label, val]) => (
                    <div key={String(label)} className="min-w-0">
                      <div className="text-[10px] font-semibold uppercase tracking-wide text-slate-500 mb-0.5">{label}</div>
                      <div className="text-sm text-slate-100 font-mono tabular-nums truncate">{val ?? "—"}</div>
                    </div>
                  ))}
              </div>
              <div className="px-3 pb-3 space-y-1">
                {s.bha_type && <div className="text-xs text-slate-300">{s.bha_type}</div>}
                <div className="text-[11px] text-slate-400 leading-snug">
                  Bit: <span className="text-slate-200">{s.primary_bit || "—"}</span>
                  {s.primary_bit_tfa ? <span className="text-slate-500"> (TFA {s.primary_bit_tfa})</span> : null}
                </div>
              </div>
            </div>
          );
        })}
      </div>
    </section>
  );
}

function RecommendedActions({ well }: { well: Well }) {
  const actions: string[] = [];
  well.drilling_fluids?.forEach((f: any) => { if (f.comments?.includes("OWR")) actions.push("Watch OWR on trips — key indicator for waterflows."); });
  well.drilling_fluids?.forEach((f: any) => { if (f.comments?.includes("MPD")) actions.push("Use MPD before increasing MW for BG/connection gas."); });
  well.drilling_sections?.forEach((s: any) => { if (s.comments?.includes("Kick off")) actions.push(`${s.section_name}: Kick off ASAP out of shoe.`); });
  well.anti_collision?.filter((ac: any) => ac.sf < ac.sf_req * 1.3).forEach((ac: any) => {
    actions.push(`Maintain SF >${ac.sf_req} against ${ac.ref_well.split("(")[0].trim()} at TD.`);
  });
  if (well.directional?.notes?.includes("ICP")) actions.push("Min ICP setting depth = 50' into target formation carbonate.");
  if (!actions.length) return null;
  return (
    <section className="rounded-xl border border-violet-500/25 bg-violet-950/20 p-4 shadow-sm">
      <h4 className="text-xs font-bold uppercase tracking-wide text-violet-200 mb-3 flex items-center gap-2">
        <Sparkles className="h-4 w-4 shrink-0" /> Recommended actions
      </h4>
      <ul className="space-y-2 list-none">
        {[...new Set(actions)].map((a, i) => (
          <li key={i} className="px-3.5 py-2.5 rounded-lg text-sm text-slate-200 leading-relaxed border border-violet-500/15 bg-slate-950/40">
            {a}
          </li>
        ))}
      </ul>
    </section>
  );
}

// ═══════════════════════════════════════════════════════════════
//  BOTTOM WORKSPACE TABS
// ═══════════════════════════════════════════════════════════════
function BottomWorkspace({ well, wells, data, tab, setTab }: { well: Well; wells: Well[]; data: WellPlanData; tab: BottomTab; setTab: (t: BottomTab) => void }) {
  const TABS: { id: BottomTab; label: string; icon: React.ReactNode }[] = [
    { id: "overview", label: "Overview", icon: <FileText className="h-3 w-3" /> },
    { id: "casing", label: "Casing & Cement", icon: <Ruler className="h-3 w-3" /> },
    { id: "bha", label: "BHA Program", icon: <Wrench className="h-3 w-3" /> },
    { id: "envelope", label: "Drilling Envelope", icon: <Gauge className="h-3 w-3" /> },
    { id: "hazards", label: "Hazards", icon: <AlertTriangle className="h-3 w-3" /> },
    { id: "offsets", label: "Offset Wells", icon: <Crosshair className="h-3 w-3" /> },
    { id: "params", label: "Parameters", icon: <Activity className="h-3 w-3" /> },
  ];

  return (
    <div className="flex-shrink-0 border-t border-slate-700/50 bg-slate-950/80" style={{ height: 240 }}>
      {/* Tab bar */}
      <div className="flex gap-1 px-4 pt-2 pb-0 overflow-x-auto border-b border-slate-800/90">
        {TABS.map((t) => (
          <button
            key={t.id}
            type="button"
            onClick={() => setTab(t.id)}
            className={`flex items-center gap-2 px-3 py-2 rounded-t-lg text-xs font-medium transition-all border border-b-0 whitespace-nowrap ${
              tab === t.id
                ? "text-cyan-200 bg-slate-900 border-slate-600 border-b-transparent -mb-px z-[1]"
                : "text-slate-500 border-transparent hover:text-slate-200 hover:bg-slate-900/40"
            }`}
          >
            <span className="opacity-80">{t.icon}</span>
            {t.label}
          </button>
        ))}
      </div>
      {/* Tab content */}
      <div className="px-4 py-4 overflow-y-auto h-[calc(100%-2.75rem)] custom-scrollbar text-sm">
        {tab === "overview" && <OverviewTab well={well} data={data} />}
        {tab === "casing" && <CasingTab well={well} />}
        {tab === "bha" && <BHATab well={well} />}
        {tab === "envelope" && <EnvelopeTab well={well} />}
        {tab === "hazards" && <HazardsTab well={well} />}
        {tab === "offsets" && <OffsetsTab well={well} />}
        {tab === "params" && <ParamsTab well={well} />}
      </div>
    </div>
  );
}

// ── Tab: Overview ──
function OverviewTab({ well, data }: { well: Well; data: WellPlanData }) {
  return (
    <div className="grid grid-cols-1 lg:grid-cols-4 gap-4">
      <div className="lg:col-span-2 rounded-xl p-4 border border-cyan-500/20 bg-cyan-950/20">
        <h5 className="text-xs font-bold uppercase tracking-wide text-cyan-200 mb-2">Mission objective</h5>
        <p className="text-slate-300 leading-relaxed">
          Drill a {well.lateral_length} lateral in the <strong className="text-slate-100">{well.target_formation}</strong> formation. Design:{" "}
          <span className="text-slate-200">{well.design}</span>. Pad:{" "}
          <span className="text-slate-200">{data.wells?.length ?? "—"}</span> wells.
        </p>
      </div>
      <div className="rounded-xl p-4 border border-amber-500/20 bg-amber-950/15">
        <h5 className="text-xs font-bold uppercase tracking-wide text-amber-200 mb-2">Risk posture</h5>
        <p className="text-slate-300 leading-relaxed">
          {well.risks_and_hazards?.length ?? 0} identified risks. Review hazards tab for mitigations and context.
        </p>
      </div>
      <div className="rounded-xl p-4 border border-violet-500/20 bg-violet-950/15">
        <h5 className="text-xs font-bold uppercase tracking-wide text-violet-200 mb-2">Approvals</h5>
        <div className="space-y-2 max-h-32 overflow-y-auto custom-scrollbar pr-1">
          {data.approvals?.map((a: any, i: number) => (
            <div key={i} className="text-xs text-slate-300 leading-snug border-b border-slate-700/50 last:border-0 pb-2 last:pb-0">
              <span className="text-slate-100 font-medium">{a.name}</span>
              <span className="text-slate-500"> — </span>
              <span>{a.action}</span>
              {a.datetime ? <div className="text-[11px] text-slate-500 mt-0.5 font-mono">{a.datetime}</div> : null}
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}

// ── Tab: Casing & Cement ──
function CasingTab({ well }: { well: Well }) {
  return (
    <div className="flex flex-col lg:flex-row gap-6">
      <div className="flex-1 min-w-0">
        <h5 className="text-xs font-bold uppercase tracking-wide text-cyan-200 mb-3">Casing strings</h5>
        <div className="space-y-2">
          {well.casing_program?.map((c: any, i: number) => {
            const clr = c.section_name?.includes("Surface") ? "#fbbf24" : c.section_name?.includes("Inter") ? "#22d3ee" : "#4ade80";
            return (
              <div key={i} className="flex items-center gap-3 px-3 py-2.5 rounded-xl border border-slate-700/60 bg-slate-900/40" style={{ boxShadow: `inset 3px 0 0 0 ${clr}` }}>
                <div className="flex-1 grid grid-cols-2 sm:grid-cols-5 gap-2 text-xs">
                  <span className="font-semibold text-slate-100 sm:col-span-1 col-span-2">
                    {c.casing_od ?? c.casing_od_in}" {c.section_name}
                  </span>
                  <span className="text-slate-400">{c.grade}</span>
                  <span className="text-slate-300 font-mono tabular-nums">{c.weight_lbm_ft ?? c.weight_per_length}#</span>
                  <span className="text-slate-400">{c.connection}</span>
                  <span className="text-slate-300 font-mono tabular-nums sm:col-span-1 col-span-2">
                    {(c.start_md ?? c.start_md_ft)?.toLocaleString()}→{(c.end_md ?? c.end_md_ft)?.toLocaleString()}′
                  </span>
                </div>
              </div>
            );
          })}
        </div>
      </div>
      <div className="flex-1 min-w-0">
        <h5 className="text-xs font-bold uppercase tracking-wide text-slate-300 mb-3">Cement program</h5>
        <div className="space-y-2">
          {well.cement_program?.map((c: any, i: number) => (
            <div key={i} className="px-3 py-2.5 rounded-xl text-xs border border-slate-700/60 bg-slate-900/40">
              <div className="flex justify-between gap-2">
                <span className="text-slate-100 font-medium">{c.casing}</span>
                <span className="text-slate-500 shrink-0">{c.type}</span>
              </div>
              <div className="text-slate-400 font-mono mt-1 leading-relaxed">
                {c.weight} ppg
                {c.volume_bbl ? ` · ${c.volume_bbl} bbl · ${c.sacks} sacks · ${c.excess_pct}% excess` : ""}
                {c.top_md ? ` · Top: ${c.top_md}′` : ""}
              </div>
            </div>
          )) || <span className="text-slate-500 text-sm">No cement data</span>}
        </div>
      </div>
    </div>
  );
}

// ── Tab: BHA Program ──
function BHATab({ well }: { well: Well }) {
  const colors = ["#fbbf24", "#c4b5fd", "#4ade80"];
  return (
    <div className="grid grid-cols-1 md:grid-cols-3 gap-4">
      {well.drilling_sections?.map((s: any, i: number) => {
        const c = colors[i % 3];
        return (
          <div key={i} className="rounded-xl overflow-hidden border border-slate-700/60 bg-slate-900/40" style={{ boxShadow: `inset 0 -1px 0 0 ${c}33` }}>
            <div className="px-3 py-2 border-b border-slate-700/50" style={{ background: `${c}14` }}>
              <div className="text-sm font-semibold" style={{ color: c }}>
                {s.section_name}{" "}
                <span className="text-slate-500 font-mono font-normal text-xs">{s.hole_size ?? s.hole_size_in}&Prime;</span>
              </div>
            </div>
            <div className="px-3 py-3 space-y-2 text-xs">
              <div className="text-slate-300 font-medium">{s.bha_type}</div>
              <div className="grid grid-cols-2 gap-2">
                <div className="rounded-lg px-2 py-2 bg-slate-950/50 border border-slate-700/50">
                  <div className="text-[10px] font-semibold text-slate-500 uppercase tracking-wide mb-1">Primary</div>
                  <div className="text-slate-200">{s.primary_bit}</div>
                  {s.primary_bit_tfa && <div className="text-slate-400 font-mono text-[11px] mt-0.5">TFA {s.primary_bit_tfa}</div>}
                </div>
                <div className="rounded-lg px-2 py-2 bg-slate-950/50 border border-slate-700/50">
                  <div className="text-[10px] font-semibold text-slate-500 uppercase tracking-wide mb-1">Backup</div>
                  <div className="text-slate-200">{s.backup_bit}</div>
                  {s.backup_bit_tfa && <div className="text-slate-400 font-mono text-[11px] mt-0.5">TFA {s.backup_bit_tfa}</div>}
                </div>
              </div>
              {s.bha_details && <div className="text-[11px] text-slate-400 leading-relaxed border-t border-slate-700/50 pt-2">{s.bha_details}</div>}
            </div>
          </div>
        );
      })}
    </div>
  );
}

// ── Tab: Drilling Envelope ──
function EnvelopeTab({ well }: { well: Well }) {
  return (
    <div className="space-y-5">
      {well.drilling_sections?.map((s: any, i: number) => {
        const params = [
          { label: "WOB", value: s.wob_klbf ?? s.wob, unit: "klbf", color: "#22d3ee" },
          { label: "RPM", value: s.rpm, unit: "RPM", color: "#4ade80" },
          { label: "Flow", value: s.flow_rate_gpm ?? s.flow_rate, unit: "gpm", color: "#fbbf24" },
          { label: "ROP", value: s.rop_fth ?? s.rop, unit: "ft/h", color: "#c4b5fd" },
          { label: "DiffP", value: s.diffp_max_psi ?? s.diffp_max, unit: "psi", color: "#fb7185" },
        ];
        const df = s.depth_from ?? s.depth_from_ft;
        const dt = s.depth_to ?? s.depth_to_ft;
        const hs = s.hole_size ?? s.hole_size_in;
        return (
          <div key={i}>
            <div className="text-sm font-semibold text-slate-200 mb-2">
              {s.section_name}{" "}
              <span className="text-slate-500 font-mono font-normal">
                ({hs != null ? `${hs}" · ` : ""}
                {df != null ? Number(df).toLocaleString() : "—"}→{dt != null ? Number(dt).toLocaleString() : "—"}′)
              </span>
            </div>
            <div className="grid grid-cols-2 sm:grid-cols-3 lg:grid-cols-5 gap-2">
              {params.map((p, j) => (
                <div key={j} className="rounded-xl p-3 text-center border border-slate-700/60 bg-slate-900/50" style={{ boxShadow: `inset 0 2px 0 0 ${p.color}44` }}>
                  <div className="text-[10px] font-semibold text-slate-500 uppercase tracking-wide mb-1">{p.label}</div>
                  <div className="text-base font-bold font-mono tabular-nums" style={{ color: p.color }}>
                    {p.value ?? "—"}
                  </div>
                  <div className="text-[11px] text-slate-500 mt-0.5">{p.unit}</div>
                </div>
              ))}
            </div>
          </div>
        );
      })}
    </div>
  );
}

// ── Tab: Hazards & Mitigations ──
function HazardsTab({ well }: { well: Well }) {
  const getSeverity = (r: any) => {
    const t = r.risk.toLowerCase();
    return t.includes("loss") || t.includes("water") || t.includes("gas") ? "High" : "Medium";
  };
  return (
    <div className="overflow-x-auto rounded-xl border border-slate-700/60">
      <table className="w-full text-xs min-w-[640px]">
        <thead>
          <tr className="text-left bg-slate-900/80 text-slate-400 border-b border-slate-700/80">
            <th className="py-2.5 px-3 font-semibold">Risk</th>
            <th className="py-2.5 px-3 font-semibold">Section</th>
            <th className="py-2.5 px-3 font-semibold">Severity</th>
            <th className="py-2.5 px-3 font-semibold">Impact</th>
            <th className="py-2.5 px-3 font-semibold">Mitigation</th>
          </tr>
        </thead>
        <tbody>
          {well.risks_and_hazards?.map((r: any, i: number) => (
            <tr key={i} className="border-t border-slate-800/90 hover:bg-slate-900/40">
              <td className="py-2.5 px-3 text-slate-100 font-medium max-w-[200px]">{r.risk}</td>
              <td className="py-2.5 px-3 text-slate-400">{r.section}</td>
              <td className="py-2.5 px-3">
                <span
                  className={`inline-flex px-2 py-0.5 rounded-md text-[11px] font-semibold ${
                    getSeverity(r) === "High" ? "bg-red-500/15 text-red-200 border border-red-500/25" : "bg-amber-500/15 text-amber-100 border border-amber-500/25"
                  }`}
                >
                  {getSeverity(r)}
                </span>
              </td>
              <td className="py-2.5 px-3 text-slate-400">
                {r.risk.includes("Loss") ? "NPT / losses" : r.risk.includes("Water") ? "Stability" : r.risk.includes("Gas") ? "Pressure" : "Efficiency"}
              </td>
              <td className="py-2.5 px-3 text-slate-300 leading-snug max-w-md">{r.comments}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  );
}

// ── Tab: Offset Wells ──
function OffsetsTab({ well }: { well: Well }) {
  if (!well.anti_collision?.length) return <div className="text-sm text-slate-500">No anti-collision data</div>;
  return (
    <div className="space-y-2">
      {well.anti_collision.map((ac: any, i: number) => {
        const pct = Math.min((ac.sf / (ac.sf_req * 2)) * 100, 100);
        const isTight = ac.sf < ac.sf_req * 1.3;
        const clr = isTight ? "#EF4444" : ac.sf < ac.sf_req * 2 ? "#F59E0B" : "#10B981";
        return (
          <div key={i} className="flex items-center gap-4 px-3 py-2 rounded-lg" style={{ background: `${clr}04`, border: `1px solid ${clr}12` }}>
            <div className="w-28 text-xs font-medium text-slate-200 truncate" title={ac.ref_well}>{ac.ref_well}</div>
            <div className="flex-1">
              <div className="h-2 rounded-full overflow-hidden" style={{ background: "rgba(255,255,255,0.04)" }}>
                <div className="h-full rounded-full transition-all" style={{ width: `${pct}%`, background: clr }} />
              </div>
            </div>
            <div className="text-right w-40 grid grid-cols-3 gap-2 text-[8px] font-mono">
              <span style={{ color: clr }}>SF {ac.sf.toFixed(2)}</span>
              <span className="text-slate-400">CtC {ac.ctc}</span>
              <span className="text-slate-400">EtE {ac.ete}</span>
            </div>
            <span className={`text-[7px] px-1.5 py-0.5 rounded ${isTight ? "bg-red-500/15 text-red-400" : "bg-green-500/10 text-green-400"}`}>
              {isTight ? "WATCH" : "OK"}
            </span>
          </div>
        );
      })}
    </div>
  );
}

// ── Tab: Parameters ──
function ParamsTab({ well }: { well: Well }) {
  return (
    <div className="grid grid-cols-3 gap-4">
      {/* Wellhead */}
      {well.wellhead && (
        <div className="rounded-lg p-3" style={{ background: "rgba(255,255,255,0.02)", border: "1px solid rgba(255,255,255,0.04)" }}>
          <h5 className="text-xs font-bold text-slate-300 mb-2">Wellhead</h5>
          {Object.entries(well.wellhead).map(([k, v]) => (
            <div key={k} className="text-xs text-slate-300 py-0.5">
              <span className="text-slate-500">{k.replace(/_/g, " ")}:</span> {String(v)}
            </div>
          ))}
        </div>
      )}
      {/* FIT */}
      {well.fit_data?.length > 0 && (
        <div className="rounded-lg p-3" style={{ background: "rgba(255,255,255,0.02)", border: "1px solid rgba(255,255,255,0.04)" }}>
          <h5 className="text-xs font-bold text-slate-300 mb-2">FIT data</h5>
          {well.fit_data.map((f: any, i: number) => (
            <div key={i} className="text-xs text-slate-300 py-0.5">
              {f.section}: <span className="font-mono text-slate-200">{f.fit_emw} EMW · {f.surface_psi} psi</span>
            </div>
          ))}
        </div>
      )}
      {/* Directional */}
      {well.directional && (
        <div className="rounded-lg p-3" style={{ background: "rgba(139,92,246,0.02)", border: "1px solid rgba(139,92,246,0.06)" }}>
          <h5 className="text-xs font-bold mb-2 text-violet-200">Directional</h5>
          <div className="text-xs text-slate-300 leading-relaxed">{well.directional.notes}</div>
          {well.directional.curve_dls && <div className="text-xs text-slate-400 font-mono mt-1">DLS: {well.directional.curve_dls}</div>}
        </div>
      )}
    </div>
  );
}
