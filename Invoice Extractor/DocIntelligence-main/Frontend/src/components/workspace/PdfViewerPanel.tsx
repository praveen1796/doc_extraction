// Add file prop to PdfViewerPanel and render real PDF via blob URL

import { motion } from "framer-motion";
import { useState, useEffect, useMemo } from "react";
import { ZoomIn, ZoomOut, Maximize2, Minimize2, Loader2, PanelLeftClose } from "lucide-react";

interface PdfViewerPanelProps {
  file?: File | null;
  isComplete?: boolean;
  progress?: number;
  isExpanded?: boolean;
  onToggleExpand?: () => void;
  /** Auto-hide left panel (VS-style); more room for extracted data */
  onCollapseSidePanel?: () => void;
}

export function PdfViewerPanel({
  file,
  isComplete = true,
  progress = 100,
  isExpanded = false,
  onToggleExpand,
  onCollapseSidePanel,
}: PdfViewerPanelProps) {
  const [zoom, setZoom] = useState(100);

  // Create blob URL for real PDF
  const pdfUrl = useMemo(() => {
    if (!file || file.size < 100) return null; // skip demo blobs
    if (file.type === "application/pdf" || file.name.toLowerCase().endsWith(".pdf")) {
      return URL.createObjectURL(file);
    }
    return null;
  }, [file]);

  // Cleanup blob URL
  useEffect(() => { return () => { if (pdfUrl) URL.revokeObjectURL(pdfUrl); }; }, [pdfUrl]);

  const hasRealPdf = !!pdfUrl;

  return (
    <motion.div
      initial={{ opacity: 0, x: -16 }}
      animate={{ opacity: 1, x: 0 }}
      transition={{ duration: 0.4, delay: 0.1 }}
      className="glass-panel flex flex-col h-full overflow-hidden"
    >
      {/* Toolbar */}
      <div className="flex items-center justify-between px-4 py-2.5 border-b border-border/50">
        <div className="flex items-center gap-2">
          <span className="text-xs font-medium text-muted-foreground">
            {file?.name ? file.name.slice(0, 40) + (file.name.length > 40 ? "…" : "") : "Document Preview"}
          </span>
          {!isComplete && (
            <div className="flex items-center gap-1.5 ml-2">
              <Loader2 className="h-3 w-3 text-primary animate-spin" />
              <span className="text-[10px] text-primary font-medium">Scanning…</span>
            </div>
          )}
        </div>
        <div className="flex items-center gap-1">
          {!hasRealPdf && <>
            <button onClick={() => setZoom((z) => Math.max(50, z - 25))} className="p-1.5 rounded-md hover:bg-secondary/60 text-muted-foreground hover:text-foreground transition-colors"><ZoomOut className="h-3.5 w-3.5" /></button>
            <span className="text-xs text-muted-foreground px-1 min-w-[36px] text-center">{zoom}%</span>
            <button onClick={() => setZoom((z) => Math.min(200, z + 25))} className="p-1.5 rounded-md hover:bg-secondary/60 text-muted-foreground hover:text-foreground transition-colors"><ZoomIn className="h-3.5 w-3.5" /></button>
          </>}
          {onCollapseSidePanel && !isExpanded && (
            <button
              type="button"
              onClick={onCollapseSidePanel}
              title="Hide document panel — maximize extracted data (Ctrl+Alt+D)"
              className="p-1.5 rounded-md hover:bg-secondary/60 text-muted-foreground hover:text-foreground transition-colors"
            >
              <PanelLeftClose className="h-3.5 w-3.5" />
            </button>
          )}
          <button onClick={onToggleExpand}
            className={`p-1.5 rounded-md transition-colors ml-1 ${isExpanded ? "bg-primary/10 text-primary hover:bg-primary/20" : "hover:bg-secondary/60 text-muted-foreground hover:text-foreground"}`}>
            {isExpanded ? <Minimize2 className="h-3.5 w-3.5" /> : <Maximize2 className="h-3.5 w-3.5" />}
          </button>
        </div>
      </div>

      {/* Content */}
      <div className="flex-1 overflow-hidden relative">
        {hasRealPdf ? (
          /* ═══ REAL PDF via iframe — always clear ═══ */
          <iframe src={pdfUrl + "#toolbar=0&navpanes=0"} className="w-full h-full border-0" title="Document Preview" />
        ) : (
          /* ═══ DEMO FALLBACK: static HTML invoice ═══ */
          <div className="flex flex-1 h-full overflow-hidden">
            <div className="flex-1 p-4 overflow-auto flex items-start justify-center">
              <div className="relative w-full rounded-xl bg-white/[0.03] border border-border/30 shadow-[inset_0_2px_8px_-2px_hsl(220_50%_4%/0.5)]"
                style={{ maxWidth: isExpanded ? 800 : 560, aspectRatio: "8.5 / 11", transform: `scale(${zoom / 100})`, transformOrigin: "top center", transition: "max-width 0.3s ease, transform 0.2s ease" }}>
                <div className="absolute inset-0 p-6 text-[10px] text-muted-foreground/40 font-mono leading-relaxed select-none pointer-events-none">
                  <div className="flex justify-between mb-6">
                    <div><div className="text-sm font-bold text-muted-foreground/50 mb-1">ACME INDUSTRIAL SUPPLY CO.</div><div>1200 Commerce Blvd, Suite 400</div><div>Houston, TX 77002</div></div>
                    <div className="text-right"><div className="text-lg font-bold text-muted-foreground/50 mb-1">INVOICE</div><div>INV-2047</div><div>Date: 2026-03-18</div></div>
                  </div>
                  <div className="border-t border-muted-foreground/10 pt-4 mb-4"><div className="font-semibold text-muted-foreground/40 mb-2">Bill To:</div><div>BuildRight Construction LLC</div><div>4500 Industrial Park Drive</div><div>Dallas, TX 75201</div></div>
                  <div className="mb-2 font-semibold text-muted-foreground/40">PO Number: PO-8834</div>
                  <table className="w-full mt-4 text-[9px]"><thead><tr className="border-b border-muted-foreground/10"><th className="text-left py-1.5 font-medium">Item Description</th><th className="text-right py-1.5 font-medium">Qty</th><th className="text-right py-1.5 font-medium">Unit Price</th><th className="text-right py-1.5 font-medium">Total</th></tr></thead>
                  <tbody>{[["Steel Mounting Bracket (Type-A)","200","$24.50","$4,900.00"],["Industrial Fastener Kit #440","50","$68.00","$3,400.00"],["Hydraulic Seal Ring (12mm)","500","$3.80","$1,900.00"],["Precision Bearing Assembly","30","$89.00","$2,670.00"],["Welding Rod Bundle (E7018)","25","$52.40","$1,310.00"],["Thermal Insulation Sheet","10","$110.00","$1,100.00"]].map(([item,qty,price,total],i)=>(<tr key={i} className="border-b border-muted-foreground/5"><td className="py-1.5">{item}</td><td className="text-right py-1.5">{qty}</td><td className="text-right py-1.5">{price}</td><td className="text-right py-1.5">{total}</td></tr>))}</tbody></table>
                  <div className="flex justify-end mt-6 text-[10px]"><div className="w-48 space-y-1"><div className="flex justify-between"><span>Subtotal</span><span>$14,280.00</span></div><div className="flex justify-between"><span>Tax (8.5%)</span><span>$1,213.80</span></div><div className="flex justify-between font-bold text-muted-foreground/50 border-t border-muted-foreground/10 pt-1 mt-1"><span>Total</span><span>$15,493.80</span></div></div></div>
                </div>
              </div>
            </div>
          </div>
        )}

        {/* ═══ SCANNING OVERLAY — non-obstructive, on top of clear document ═══ */}
        {!isComplete && (
          <>
            {/* Scan beam */}
            <motion.div
              className="absolute left-0 right-0 h-[2px] pointer-events-none z-10"
              style={{ background: "linear-gradient(90deg, transparent 0%, hsl(var(--primary)) 30%, hsl(var(--primary)) 70%, transparent 100%)", boxShadow: "0 0 20px 4px hsl(var(--primary) / 0.3), 0 0 60px 8px hsl(var(--primary) / 0.1)" }}
              initial={{ top: "0%" }}
              animate={{ top: ["0%", "100%", "0%"] }}
              transition={{ duration: 5, repeat: Infinity, ease: "linear" }}
            />
            {/* Subtle edge glow */}
            <div className="absolute inset-0 pointer-events-none z-10 rounded-lg" style={{
              boxShadow: "inset 0 0 30px -10px hsl(var(--primary) / 0.08)",
              border: "1px solid hsl(var(--primary) / 0.06)",
            }} />
            {/* Corner brackets */}
            {[
              "top-2 left-2 border-t-2 border-l-2 rounded-tl",
              "top-2 right-2 border-t-2 border-r-2 rounded-tr",
              "bottom-2 left-2 border-b-2 border-l-2 rounded-bl",
              "bottom-2 right-2 border-b-2 border-r-2 rounded-br",
            ].map((pos, i) => (
              <div key={i} className={`absolute w-6 h-6 ${pos} pointer-events-none z-10`}
                style={{ borderColor: "hsl(var(--primary) / 0.2)" }}>
                <motion.div className="absolute inset-0" style={{ borderColor: "hsl(var(--primary) / 0.5)" }}
                  animate={{ opacity: [0.3, 0.8, 0.3] }} transition={{ duration: 2, repeat: Infinity, delay: i * 0.4 }} />
              </div>
            ))}
          </>
        )}
      </div>
    </motion.div>
  );
}
