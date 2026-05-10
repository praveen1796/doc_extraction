import { useState, useMemo, useEffect, useCallback } from "react";
import { motion, AnimatePresence } from "framer-motion";

// ═══════════════════════════════════════════════════════════════
//  INTERACTIVE WELL INTELLIGENCE CANVAS
//  - Animated progressive drill path
//  - Hover depth tracker with MD/TVD readout
//  - Risk overlay mode (formations glow by hazard)  
//  - Target zone glowing highlight
//  - KOP + landing markers
//  - Cross-panel: onHoverSection callback
// ═══════════════════════════════════════════════════════════════

interface FormationTop { formation_name: string; md: number | null; tvd: number | null; }
interface CasingSection { section_name: string; hole_size: number | null; casing_od: number | null; grade: string; weight_per_length: number | null; start_md: number | null; end_md: number | null; }
interface DrillingFluid { section: string; fluid_type: string; design_mw: number | null; }

interface Props {
  wellName: string;
  totalDepthMD: number;
  totalDepthTVD: number;
  formationTops: FormationTop[];
  casingProgram: CasingSection[];
  drillingFluids: DrillingFluid[];
  lateralLength?: string;
  riskOverlay?: boolean;
  risks?: { section: string; risk: string; comments: string }[];
  highlightSection?: string | null;
  onHoverFormation?: (idx: number | null) => void;
}

const FM_PALETTE = [
  { fill: "#7B6B52", pat: "dots" },   { fill: "#B8A57A", pat: "halite" },
  { fill: "#8B5E3C", pat: "brick" },  { fill: "#5A7A5A", pat: "wavy" },
  { fill: "#C4A872", pat: "dots" },   { fill: "#7A5A14", pat: "cross" },
  { fill: "#4A6030", pat: "wavy" },   { fill: "#B07A3A", pat: "brick" },
  { fill: "#607080", pat: "wavy" },   { fill: "#BA9520", pat: "dots" },
  { fill: "#A0A060", pat: "cross" },  { fill: "#8B5E3C", pat: "brick" },
  { fill: "#5A7A5A", pat: "wavy" },   { fill: "#B06020", pat: "dots" },
  { fill: "#B07A3A", pat: "brick" },  { fill: "#707020", pat: "wavy" },
  { fill: "#BA9520", pat: "cross" },  { fill: "#7A4020", pat: "brick" },
];

const CASING_CLR: Record<string, string> = { surface: "#F59E0B", intermediate: "#06B6D4", production: "#10B981", upper: "#06B6D4", lower: "#0891B2", "p110": "#06B6D4", "l80": "#0891B2", "5.5": "#10B981", "9.625": "#F59E0B" };
function getCc(n: string) { const l = n.toLowerCase(); for (const [k, c] of Object.entries(CASING_CLR)) if (l.includes(k)) return c; return "#06B6D4"; }

export function DigitalWellTwin({ wellName, totalDepthMD, totalDepthTVD, formationTops, casingProgram, drillingFluids, lateralLength, riskOverlay, risks, highlightSection, onHoverFormation }: Props) {
  const [hovFm, setHovFm] = useState<number | null>(null);
  const [hovCs, setHovCs] = useState<number | null>(null);
  const [mouseY, setMouseY] = useState<number | null>(null);
  const [drillProg, setDrillProg] = useState(0);
  const [svgRect, setSvgRect] = useState<DOMRect | null>(null);

  // Progressive drill animation
  useEffect(() => {
    let frame: number;
    let start: number | null = null;
    const dur = 3000;
    const animate = (ts: number) => {
      if (!start) start = ts;
      const p = Math.min((ts - start) / dur, 1);
      setDrillProg(p);
      if (p < 1) frame = requestAnimationFrame(animate);
    };
    frame = requestAnimationFrame(animate);
    return () => cancelAnimationFrame(frame);
  }, [wellName]);

  const W = 820, H = 560;
  const EL = 60, ER = 520, EW = ER - EL;
  const WCX = EL + EW * 0.42;
  const SY = 72, VB = 390;
  const CEX = EL + EW * 0.75, CEY = 425;
  const LEX = W - 40, LY = 425;
  const maxTVD = Math.max(...formationTops.map(f => f.tvd ?? 0), totalDepthTVD);
  const tvd2y = (tvd: number) => SY + (tvd / maxTVD) * (VB - SY);
  const y2tvd = (y: number) => Math.round(((y - SY) / (VB - SY)) * maxTVD);

  // Formation bands
  const bands = useMemo(() => {
    if (!formationTops.length) return [];
    const sorted = [...formationTops].sort((a, b) => (a.tvd ?? 0) - (b.tvd ?? 0));
    return sorted.map((f, i) => {
      const y1 = tvd2y(f.tvd ?? 0);
      const y2 = i < sorted.length - 1 ? tvd2y(sorted[i + 1].tvd ?? 0) : VB + 15;
      const pal = FM_PALETTE[i % FM_PALETTE.length];
      const isTarget = f.formation_name.includes("TARGET") || f.formation_name === formationTops[formationTops.length - 1]?.formation_name;
      return { ...f, y1, y2, ...pal, idx: i, isTarget };
    });
  }, [formationTops, maxTVD]);

  // Risk zones mapping
  const riskZones = useMemo(() => {
    if (!riskOverlay || !risks) return [];
    return risks.map(r => {
      const isInt = r.section.toLowerCase().includes("inter");
      const y1 = isInt ? SY : VB - 60;
      const y2 = isInt ? VB * 0.6 : VB + 15;
      const clr = r.risk.toLowerCase().includes("loss") ? "#F59E0B" : r.risk.toLowerCase().includes("water") ? "#3B82F6" : r.risk.toLowerCase().includes("gas") ? "#EF4444" : "#8B5CF6";
      return { ...r, y1, y2, clr };
    });
  }, [riskOverlay, risks]);

  const handleSvgRef = useCallback((el: SVGSVGElement | null) => {
    if (el) setSvgRect(el.getBoundingClientRect());
  }, []);

  const handleMouseMove = useCallback((e: React.MouseEvent<SVGSVGElement>) => {
    if (!svgRect) return;
    const y = ((e.clientY - svgRect.top) / svgRect.height) * H;
    if (y >= SY && y <= VB) setMouseY(y); else setMouseY(null);
  }, [svgRect, H]);

  const hoverFm = (i: number | null) => { setHovFm(i); onHoverFormation?.(i); };

  // Highlight section mapping
  const hlY = highlightSection ? (
    highlightSection === "Intermediate" ? { y1: SY + 30, y2: VB - 20 } :
    highlightSection === "Curve" ? { y1: VB - 20, y2: CEY } :
    highlightSection === "Lateral" ? { y1: LY - 8, y2: LY + 8 } : null
  ) : null;

  return (
    <div className="relative h-full w-full">
      <svg ref={handleSvgRef} viewBox={`0 0 ${W} ${H}`} className="w-full h-full" preserveAspectRatio="xMidYMid meet"
        onMouseMove={handleMouseMove} onMouseLeave={() => { setMouseY(null); hoverFm(null); setHovCs(null); }}>
        <defs>
          <pattern id="pd" width="8" height="8" patternUnits="userSpaceOnUse"><circle cx="2" cy="2" r="0.7" fill="rgba(0,0,0,0.12)" /><circle cx="6" cy="6" r="0.5" fill="rgba(0,0,0,0.08)" /></pattern>
          <pattern id="pw" width="12" height="6" patternUnits="userSpaceOnUse"><path d="M0 3Q3 1 6 3Q9 5 12 3" fill="none" stroke="rgba(0,0,0,0.1)" strokeWidth="0.7" /></pattern>
          <pattern id="pb" width="10" height="8" patternUnits="userSpaceOnUse"><line x1="0" y1="4" x2="10" y2="4" stroke="rgba(0,0,0,0.08)" strokeWidth="0.4" /><line x1="5" y1="0" x2="5" y2="4" stroke="rgba(0,0,0,0.06)" strokeWidth="0.4" /></pattern>
          <pattern id="pc" width="8" height="8" patternUnits="userSpaceOnUse"><line x1="0" y1="4" x2="8" y2="4" stroke="rgba(0,0,0,0.06)" strokeWidth="0.3" /><line x1="4" y1="0" x2="4" y2="8" stroke="rgba(0,0,0,0.06)" strokeWidth="0.3" /></pattern>
          <pattern id="ph" width="10" height="10" patternUnits="userSpaceOnUse"><rect x="1" y="1" width="3" height="3" fill="rgba(255,255,255,0.1)" rx="0.5" /><rect x="6" y="6" width="2" height="2" fill="rgba(255,255,255,0.07)" rx="0.3" /></pattern>
          <linearGradient id="lglow" x1="0" y1="0" x2="1" y2="0"><stop offset="0%" stopColor="#10B981" stopOpacity="0.8" /><stop offset="100%" stopColor="#10B981" stopOpacity="0.1" /></linearGradient>
          <filter id="gs"><feGaussianBlur stdDeviation="1.5" result="b" /><feMerge><feMergeNode in="b" /><feMergeNode in="SourceGraphic" /></feMerge></filter>
          <filter id="gl"><feGaussianBlur stdDeviation="3" result="b" /><feMerge><feMergeNode in="b" /><feMergeNode in="SourceGraphic" /></feMerge></filter>
          <filter id="gxl"><feGaussianBlur stdDeviation="6" result="b" /><feMerge><feMergeNode in="b" /><feMergeNode in="SourceGraphic" /></feMerge></filter>
          <clipPath id="ec"><rect x={EL} y={SY} width={EW} height={VB - SY + 50} rx="3" /></clipPath>
        </defs>

        {/* Background */}
        <rect width={W} height={H} fill="#060d19" rx="10" />
        {/* Subtle grid */}
        <g opacity="0.03">{Array.from({ length: 20 }, (_, i) => <line key={`h${i}`} x1={EL} y1={SY + i * 20} x2={ER} y2={SY + i * 20} stroke="#06B6D4" strokeWidth="0.5" />)}
        {Array.from({ length: 25 }, (_, i) => <line key={`v${i}`} x1={EL + i * 20} y1={SY} x2={EL + i * 20} y2={VB + 40} stroke="#06B6D4" strokeWidth="0.5" />)}</g>

        {/* Ground surface */}
        <rect x={EL - 5} y={SY - 6} width={EW + 10} height="8" fill="#2d4a15" rx="2" />
        {Array.from({ length: 18 }, (_, i) => <g key={i} transform={`translate(${EL + i * 26 + 5}, ${SY - 8})`}><line x1="0" y1="3" x2="-1.5" y2="-1" stroke="#4ade80" strokeWidth="1" strokeLinecap="round" /><line x1="2" y1="3" x2="3.5" y2="-0.5" stroke="#22c55e" strokeWidth="0.8" strokeLinecap="round" /></g>)}

        {/* ═══ DERRICK ═══ */}
        <g transform={`translate(${WCX}, ${SY - 6})`}>
          <rect x="-16" y="-3" width="32" height="5" fill="#374151" rx="1" />
          <polygon points="-12,0 12,0 3.5,-48 -3.5,-48" fill="#0f172a" stroke="#64748b" strokeWidth="1" opacity="0.8" />
          <line x1="-8" y1="-10" x2="8" y2="-24" stroke="#475569" strokeWidth="0.6" /><line x1="8" y1="-10" x2="-8" y2="-24" stroke="#475569" strokeWidth="0.6" />
          <line x1="-5.5" y1="-24" x2="5.5" y2="-38" stroke="#475569" strokeWidth="0.6" /><line x1="5.5" y1="-24" x2="-5.5" y2="-38" stroke="#475569" strokeWidth="0.6" />
          <rect x="-4" y="-50" width="8" height="3" fill="#475569" rx="0.5" />
          <line x1="0" y1="-47" x2="0" y2="0" stroke="#cbd5e1" strokeWidth="0.6" />
          <circle cx="0" cy="-52" r="1.8" fill="#ef4444" filter="url(#gs)"><animate attributeName="opacity" values="1;0.3;1" dur="2s" repeatCount="indefinite" /></circle>
        </g>

        {/* ═══ FORMATION LAYERS ═══ */}
        <g clipPath="url(#ec)">
          {bands.map((b, i) => {
            const h = Math.max(b.y2 - b.y1, 2);
            const isHov = hovFm === i;
            const patMap: Record<string, string> = { dots: "pd", wavy: "pw", brick: "pb", cross: "pc", halite: "ph" };
            const isRiskZone = riskOverlay && riskZones.some(rz => b.y1 < rz.y2 && b.y2 > rz.y1);
            return (
              <g key={i} onMouseEnter={() => hoverFm(i)} onMouseLeave={() => hoverFm(null)} style={{ cursor: "pointer" }}>
                <rect x={EL} y={b.y1} width={EW} height={h} fill={b.fill} opacity={isHov ? 0.95 : riskOverlay && isRiskZone ? 0.85 : 0.55} style={{ transition: "opacity 0.25s" }} />
                <rect x={EL} y={b.y1} width={EW} height={h} fill={`url(#${patMap[b.pat]})`} />
                {/* Risk glow overlay */}
                {riskOverlay && isRiskZone && <rect x={EL} y={b.y1} width={EW} height={h} fill="rgba(239,68,68,0.08)" />}
                {/* Target zone glow */}
                {b.isTarget && <rect x={EL} y={b.y1} width={EW} height={h} fill="rgba(16,185,129,0.06)" stroke="#10B981" strokeWidth="1" strokeDasharray="4,2" filter="url(#gs)" opacity="0.8" />}
                {/* Hover highlight */}
                {isHov && <rect x={EL} y={b.y1} width={EW} height={h} fill="rgba(255,255,255,0.06)" stroke="rgba(255,255,255,0.25)" strokeWidth="0.8" />}
                {/* Labels */}
                {h > 12 && (
                  <text
                    x={EL + 8}
                    y={b.y1 + h / 2 + 4}
                    fill={isHov ? "#f8fafc" : b.isTarget ? "#6ee7b7" : "rgba(248,250,252,0.88)"}
                    fontSize={isHov ? "10" : b.isTarget ? "9.5" : "9"}
                    fontFamily="system-ui, sans-serif"
                    fontWeight={isHov || b.isTarget ? "700" : "600"}
                    style={{ transition: "all 0.2s", textShadow: "0 1px 3px rgba(0,0,0,0.95), 0 0 12px rgba(0,0,0,0.6)" }}
                  >
                    {b.formation_name.replace(" (TARGET)", "")}
                    {b.isTarget ? " ★" : ""}
                  </text>
                )}
                {h > 14 && (
                  <text x={ER - 8} y={b.y1 + h / 2 + 4} textAnchor="end" fill="rgba(226,232,240,0.9)" fontSize="8" fontFamily="ui-monospace, monospace" style={{ textShadow: "0 1px 2px rgba(0,0,0,0.9)" }}>
                    {b.tvd?.toLocaleString()}′
                  </text>
                )}
              </g>
            );
          })}
        </g>

        {/* ═══ DEPTH SCALE ═══ */}
        {[0, 2000, 4000, 6000, 8000, 10000].filter(d => d <= maxTVD).map(d => {
          const y = tvd2y(d);
          return (
            <g key={d}>
              <line x1={EL - 5} y1={y} x2={EL - 1} y2={y} stroke="rgba(148,163,184,0.45)" strokeWidth="0.7" />
              <text x={EL - 8} y={y + 3.5} textAnchor="end" fill="rgba(226,232,240,0.85)" fontSize="8" fontFamily="ui-monospace, monospace" style={{ textShadow: "0 1px 2px rgba(0,0,0,0.85)" }}>
                {(d / 1000).toFixed(0)}k
              </text>
            </g>
          );
        })}

        {/* ═══ HOVER DEPTH TRACKER ═══ */}
        {mouseY !== null && (
          <g>
            <line x1={EL} y1={mouseY} x2={ER} y2={mouseY} stroke="rgba(6,182,212,0.3)" strokeWidth="0.5" strokeDasharray="3,3" />
            <rect x={EL - 48} y={mouseY - 8} width="44" height="16" rx="3" fill="rgba(6,182,212,0.12)" stroke="rgba(6,182,212,0.3)" strokeWidth="0.5" />
            <text x={EL - 26} y={mouseY + 3.5} textAnchor="middle" fill="#e0f2fe" fontSize="8.5" fontFamily="ui-monospace, monospace" fontWeight="700" style={{ textShadow: "0 1px 2px rgba(0,0,0,0.85)" }}>{y2tvd(mouseY).toLocaleString()}′</text>
          </g>
        )}

        {/* ═══ WELLBORE ═══ */}
        <rect x={WCX - 5} y={SY} width={10} height={VB - SY} fill="rgba(10,20,35,0.9)" stroke="rgba(255,255,255,0.08)" strokeWidth="0.6" rx="1.5" />

        {/* Casing strings */}
        {casingProgram.filter(c => (c.end_md ?? 0) <= maxTVD * 1.2).map((c, i) => {
          const sy = tvd2y(c.start_md ?? 0);
          const ey = Math.min(tvd2y(c.end_md ?? maxTVD), VB);
          const clr = getCc(c.section_name);
          const xo = WCX - 7 - i * 4.5;
          const isH = hovCs === i;
          return (
            <g key={i} onMouseEnter={() => setHovCs(i)} onMouseLeave={() => setHovCs(null)} style={{ cursor: "pointer" }}>
              <rect x={xo} y={sy} width={3} height={ey - sy} fill={clr} opacity={isH ? 1 : 0.55} rx="0.5" filter={isH ? "url(#gs)" : undefined} style={{ transition: "opacity 0.2s" }} />
              <polygon points={`${xo},${ey} ${xo + 3},${ey} ${xo + 1.5},${ey + 3}`} fill={clr} opacity={isH ? 0.9 : 0.4} />
            </g>
          );
        })}

        {/* Section labels */}
        <text x={WCX} y={SY + 22} textAnchor="middle" fill="#fde68a" fontSize="9" fontWeight="800" style={{ textShadow: "0 1px 3px rgba(0,0,0,0.9), 0 0 10px rgba(245,158,11,0.35)" }}>SURFACE</text>
        <text x={WCX} y={(SY + VB) / 2} textAnchor="middle" fill="#a5f3fc" fontSize="9" fontWeight="800" style={{ textShadow: "0 1px 3px rgba(0,0,0,0.9), 0 0 10px rgba(6,182,212,0.35)" }}>INTERMEDIATE</text>

        {/* ═══ CURVE — animated ═══ */}
        <path d={`M ${WCX} ${VB} Q ${WCX + 15} ${CEY} ${CEX} ${CEY}`} fill="none" stroke="rgba(10,20,35,0.6)" strokeWidth="12" strokeLinecap="round" />
        <path d={`M ${WCX} ${VB} Q ${WCX + 15} ${CEY} ${CEX} ${CEY}`} fill="none" stroke="#8B5CF6" strokeWidth="1.5" strokeLinecap="round" filter="url(#gs)" strokeDasharray="3,3" opacity={drillProg > 0.3 ? 0.7 : 0.2} style={{ transition: "opacity 0.5s" }} />
        {/* KOP marker */}
        <circle cx={WCX} cy={VB} r="3" fill="#8B5CF6" opacity="0.6" filter="url(#gs)" />
        <text x={WCX + 14} y={VB + 4} fill="#ddd6fe" fontSize="8.5" fontWeight="800" style={{ textShadow: "0 1px 3px rgba(0,0,0,0.9)" }}>KOP</text>
        <text x={(WCX + CEX) / 2 + 12} y={CEY - 14} textAnchor="middle" fill="#ddd6fe" fontSize="9" fontWeight="800" style={{ textShadow: "0 1px 3px rgba(0,0,0,0.9), 0 0 8px rgba(139,92,246,0.45)" }}>CURVE</text>

        {/* ═══ LATERAL — progressive animation ═══ */}
        <rect x={CEX} y={LY - 5} width={(LEX - CEX) * Math.min(drillProg * 1.5, 1)} height={10} rx="2" fill="rgba(10,20,35,0.6)" stroke="rgba(16,185,129,0.08)" strokeWidth="0.3" style={{ transition: "width 0.1s" }} />
        <line x1={CEX} y1={LY} x2={CEX + (LEX - CEX) * Math.min(drillProg * 1.5, 1)} y2={LY} stroke="#10B981" strokeWidth="2" strokeLinecap="round" filter="url(#gl)" style={{ transition: "x2 0.1s" }} />
        {/* Drill bit */}
        {drillProg > 0.3 && <>
          <circle cx={CEX + (LEX - CEX) * Math.min(drillProg * 1.5, 1)} cy={LY} r="3.5" fill="#10B981" opacity="0.5" filter="url(#gl)"><animate attributeName="r" values="3;5;3" dur="1.5s" repeatCount="indefinite" /></circle>
          <circle cx={CEX + (LEX - CEX) * Math.min(drillProg * 1.5, 1)} cy={LY} r="2" fill="#10B981" />
        </>}
        {/* Landing marker */}
        <circle cx={CEX} cy={LY} r="2.5" fill="#8B5CF6" opacity="0.5" filter="url(#gs)" />
        <text x={CEX + 8} y={LY - 8} fill="#ddd6fe" fontSize="8" fontWeight="700" style={{ textShadow: "0 1px 2px rgba(0,0,0,0.9)" }}>LANDING</text>
        <text x={(CEX + LEX) / 2} y={LY - 14} textAnchor="middle" fill="#6ee7b7" fontSize="9.5" fontWeight="800" style={{ textShadow: "0 1px 3px rgba(0,0,0,0.9), 0 0 10px rgba(16,185,129,0.35)" }}>LATERAL</text>
        <text x={(CEX + LEX) / 2} y={LY + 20} textAnchor="middle" fill="#bbf7d0" fontSize="11" fontWeight="800" fontFamily="system-ui, sans-serif" style={{ textShadow: "0 1px 3px rgba(0,0,0,0.85)" }}>
          {lateralLength || `${totalDepthMD.toLocaleString()} ft`}
        </text>
        <text x={LEX} y={LY + 36} textAnchor="end" fill="rgba(226,232,240,0.82)" fontSize="8.5" fontFamily="ui-monospace, monospace" style={{ textShadow: "0 1px 2px rgba(0,0,0,0.9)" }}>
          TD: {totalDepthMD.toLocaleString()} MD / {totalDepthTVD.toLocaleString()} TVD
        </text>

        {/* ═══ SECTION HIGHLIGHT (from right panel hover) ═══ */}
        {hlY && <rect x={EL - 2} y={hlY.y1} width={EW + 4} height={hlY.y2 - hlY.y1} fill="rgba(6,182,212,0.04)" stroke="rgba(6,182,212,0.2)" strokeWidth="1" rx="3" strokeDasharray="6,3">
          <animate attributeName="stroke-opacity" values="0.2;0.5;0.2" dur="2s" repeatCount="indefinite" />
        </rect>}

        {/* ═══ RISK OVERLAY ZONES ═══ */}
        {riskOverlay && riskZones.map((rz, i) => (
          <g key={i}>
            <rect x={ER + 4} y={rz.y1} width="4" height={rz.y2 - rz.y1} fill={rz.clr} opacity="0.4" rx="2" />
            <text x={ER + 12} y={(rz.y1 + rz.y2) / 2 + 4} fill={rz.clr} fontSize="8" fontWeight="700" style={{ textShadow: "0 1px 2px rgba(0,0,0,0.95)" }}>
              {rz.risk.split("—")[0].trim()}
            </text>
          </g>
        ))}

        {/* ═══ PAD VIEW ═══ */}
        <g transform={`translate(${ER + 40}, ${SY + 5})`}>
          <text x="0" y="0" fill="rgba(226,232,240,0.85)" fontSize="8.5" fontWeight="800" letterSpacing="0.12em" style={{ textShadow: "0 1px 2px rgba(0,0,0,0.8)" }}>PAD VIEW</text>
          {[{ l: "512H", s: 1, c: "#F59E0B", w: 0.7 }, { l: "511H", s: 2, c: "#06B6D4", w: 0.8 }, { l: "510H", s: 3, c: "#10B981", w: 0.85 }, { l: "509H", s: 4, c: "#8B5CF6", w: 1 }].map((p, i) => {
            const act = wellName.includes(p.l.replace("H", ""));
            return (
              <g key={i} transform={`translate(0, ${16 + i * 20})`}>
                <rect x="0" y="-3.5" width={100 * p.w} height="12" rx="2.5" fill={act ? p.c : "rgba(255,255,255,0.02)"} opacity={act ? 0.2 : 0.08} stroke={act ? p.c : "transparent"} strokeWidth="0.6" />
                <text x="3" y="5" fill={act ? "#f8fafc" : "rgba(226,232,240,0.55)"} fontSize="8" fontWeight={act ? "800" : "600"} style={{ textShadow: act ? "0 1px 2px rgba(0,0,0,0.8)" : undefined }}>{p.s}</text>
                <rect x="12" y="-1.5" width={72 * p.w} height="8" rx="1.5" fill={p.c} opacity={act ? 0.5 : 0.12} />
                <text x={16 + 72 * p.w} y="5" fill={act ? "#f8fafc" : "rgba(203,213,225,0.65)"} fontSize="7.5" fontWeight={act ? "700" : "500"}>{p.l}</text>
              </g>
            );
          })}
        </g>

        {/* Attribution */}
        <text x={W / 2} y={H - 5} textAnchor="middle" fill="rgba(148,163,184,0.55)" fontSize="7.5" fontWeight="500">Well intelligence canvas — {wellName}</text>
      </svg>

      {/* ═══ HOVER TOOLTIP: FORMATION ═══ */}
      <AnimatePresence>
        {hovFm !== null && bands[hovFm] && (
          <motion.div initial={{ opacity: 0, y: 4 }} animate={{ opacity: 1, y: 0 }} exit={{ opacity: 0 }}
            className="absolute top-2 right-2 rounded-xl backdrop-blur-xl p-3 shadow-2xl z-20"
            style={{ minWidth: 200, background: "rgba(10,20,35,0.92)", border: `1px solid rgba(255,255,255,0.08)`, borderLeft: `3px solid ${bands[hovFm].fill}` }}>
            <div className="flex items-center gap-2 mb-2">
              <div className="w-3.5 h-3.5 rounded" style={{ background: bands[hovFm].fill }} />
              <span className="text-sm font-semibold text-slate-50">{bands[hovFm].formation_name.replace(" (TARGET)", "")}</span>
              {bands[hovFm].isTarget && <span className="text-[10px] px-1.5 py-0.5 rounded bg-emerald-500/20 text-emerald-200 font-bold border border-emerald-500/30">TARGET</span>}
            </div>
            <div className="space-y-1.5 text-xs">
              <div className="flex justify-between gap-3"><span className="text-slate-500">MD</span><span className="font-mono text-slate-200">{bands[hovFm].md?.toLocaleString()} ft</span></div>
              <div className="flex justify-between gap-3"><span className="text-slate-500">TVD</span><span className="font-mono text-slate-200">{bands[hovFm].tvd?.toLocaleString()} ft</span></div>
              {drillingFluids.length > 0 && (
                <div className="flex justify-between gap-2 pt-2 border-t border-slate-700/80">
                  <span className="text-slate-500 shrink-0">Mud</span>
                  <span className="text-slate-300 text-right leading-snug">
                    {drillingFluids[0].fluid_type.split(" ").slice(0, 4).join(" ")} ({drillingFluids[0].design_mw} ppg)
                  </span>
                </div>
              )}
            </div>
          </motion.div>
        )}
      </AnimatePresence>

      {/* ═══ HOVER TOOLTIP: CASING ═══ */}
      <AnimatePresence>
        {hovCs !== null && casingProgram[hovCs] && (
          <motion.div initial={{ opacity: 0, y: 4 }} animate={{ opacity: 1, y: 0 }} exit={{ opacity: 0 }}
            className="absolute top-2 left-2 rounded-xl backdrop-blur-xl p-3 shadow-2xl z-20"
            style={{ minWidth: 170, background: "rgba(10,20,35,0.92)", border: `1px solid rgba(255,255,255,0.08)`, borderLeft: `3px solid ${getCc(casingProgram[hovCs].section_name)}` }}>
            <div className="text-sm font-semibold text-slate-50 mb-1.5">{casingProgram[hovCs].section_name}</div>
            <div className="space-y-1 text-xs">
              <div className="flex justify-between gap-3"><span className="text-slate-500">OD</span><span className="font-mono text-slate-200">{casingProgram[hovCs].casing_od}"</span></div>
              <div className="flex justify-between gap-3"><span className="text-slate-500">Grade</span><span className="text-slate-200">{casingProgram[hovCs].grade}</span></div>
              <div className="flex justify-between gap-3"><span className="text-slate-500">Weight</span><span className="font-mono text-slate-200">{casingProgram[hovCs].weight_per_length} lbm/ft</span></div>
              <div className="flex justify-between gap-3"><span className="text-slate-500">Depth</span><span className="font-mono text-slate-300">{casingProgram[hovCs].start_md?.toLocaleString()}→{casingProgram[hovCs].end_md?.toLocaleString()}'</span></div>
            </div>
          </motion.div>
        )}
      </AnimatePresence>
    </div>
  );
}
