import { motion, AnimatePresence } from "framer-motion";
import { FileText, ChevronLeft, Loader2, CheckCircle2, Shield, Sparkles, Maximize2, Minimize2, PanelRightClose, PanelRightOpen, Download, Copy, Check, Layers, FileSpreadsheet, Upload } from "lucide-react";
import { useNavigate } from "react-router-dom";
import { documentMeta as defaultMeta } from "@/data/workspaceData";
import type { ExtractionResponse } from "@/services/api";
import {
  buildFullExtractionExport,
  copyJsonToClipboard,
  downloadJson,
  downloadExtractionXlsx,
  extractionJsonBaseName,
  extractionXlsxButtonTitle,
} from "@/lib/extractionExport";
import { useRef, useState } from "react";

interface TopActionBarProps {
  processing?: {
    isComplete: boolean;
    progress: number;
    phaseLabel: string;
  };
  documentMeta?: {
    fileName: string;
    vendor: string;
    status: string;
    confidenceLevel: string;
    processingTime: string;
  } | null;
  /** When extraction finished — enables JSON download / copy in header */
  extractionResult?: ExtractionResponse | null;
  pdfExpanded?: boolean;
  onTogglePdfExpand?: () => void;
  aiVisible?: boolean;
  onToggleAi?: () => void;
  /** Triggered when user picks a new file to re-extract in-place (same doc type). */
    onUploadNew?: (file: File) => void;
    onApprove?: () => void;
}

function ProgressRing({ progress, size = 28, stroke = 2.5 }: { progress: number; size?: number; stroke?: number }) {
  const radius = (size - stroke) / 2;
  const circumference = 2 * Math.PI * radius;
  const offset = circumference - (progress / 100) * circumference;

  return (
    <svg width={size} height={size} className="rotate-[-90deg]">
      <circle
        cx={size / 2}
        cy={size / 2}
        r={radius}
        fill="none"
        stroke="hsl(var(--primary) / 0.15)"
        strokeWidth={stroke}
      />
      <motion.circle
        cx={size / 2}
        cy={size / 2}
        r={radius}
        fill="none"
        stroke="hsl(var(--primary))"
        strokeWidth={stroke}
        strokeLinecap="round"
        strokeDasharray={circumference}
        animate={{ strokeDashoffset: offset }}
        transition={{ duration: 0.4, ease: "easeOut" }}
        style={{ filter: "drop-shadow(0 0 4px hsl(var(--primary) / 0.5))" }}
      />
    </svg>
  );
}

export function TopActionBar({
    processing,
    documentMeta,
    extractionResult,
    pdfExpanded = false,
    onTogglePdfExpand,
    aiVisible = true,
    onToggleAi,
    onUploadNew,
    onApprove, 
}: TopActionBarProps) {
  const navigate = useNavigate();
  const uploadInputRef = useRef<HTMLInputElement>(null);
  const meta = documentMeta || defaultMeta;
  const isProcessing = processing && !processing.isComplete;
  const progress = processing?.progress ?? 0;
  const [jsonCopied, setJsonCopied] = useState(false);
  const canExportJson = Boolean(extractionResult && processing?.isComplete);
  const docTypeNorm = (extractionResult?.document_type ?? "")
    .toLowerCase()
    .replace(/-/g, "_");
  const showWellTwinLink = Boolean(
    extractionResult &&
      processing?.isComplete &&
      (docTypeNorm === "well_plan" || docTypeNorm === "wellplan")
  );

  const handleDownloadJson = () => {
    if (!extractionResult) return;
    const stem = extractionJsonBaseName(meta.fileName);
    downloadJson(
      `${stem}-${extractionResult.document_type}-extraction`,
      buildFullExtractionExport(extractionResult)
    );
  };

  const handleDownloadXlsx = () => {
    if (!extractionResult?.data) return;
    downloadExtractionXlsx(extractionResult, meta.fileName);
  };

  const handleCopyJson = async () => {
    if (!extractionResult) return;
    try {
      await copyJsonToClipboard(buildFullExtractionExport(extractionResult));
      setJsonCopied(true);
      setTimeout(() => setJsonCopied(false), 2000);
    } catch {
      /* ignore */
    }
  };

  return (
    <motion.div
      initial={{ opacity: 0, y: -12 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ duration: 0.4, ease: "easeOut" }}
      className="glass-panel shrink-0 overflow-hidden"
    >
      <div className="h-12 flex items-center justify-between px-4">
        {/* Left: back + doc identity */}
        <div className="flex items-center gap-2.5 min-w-0">
          <motion.button
            onClick={() => navigate("/")}
            whileHover={{ x: -2 }}
            whileTap={{ scale: 0.92 }}
            className="p-1.5 rounded-lg hover:bg-secondary/60 transition-colors text-muted-foreground hover:text-foreground"
          >
            <ChevronLeft className="h-4 w-4" />
          </motion.button>

          <div className="relative p-1.5 rounded-lg bg-primary/10">
            <FileText className="h-3.5 w-3.5 text-primary" />
            {isProcessing && (
              <motion.div
                className="absolute inset-0 rounded-lg border border-primary/30"
                animate={{ opacity: [0.3, 0.8, 0.3] }}
                transition={{ duration: 2, repeat: Infinity, ease: "easeInOut" }}
              />
            )}
          </div>

          <div className="min-w-0">
            <h1 className="text-[13px] font-semibold text-foreground truncate max-w-[240px]">
              {meta.fileName}
            </h1>
            <AnimatePresence mode="wait">
              <motion.p
                key={isProcessing ? "processing" : "vendor"}
                initial={{ opacity: 0, y: 4 }}
                animate={{ opacity: 1, y: 0 }}
                exit={{ opacity: 0, y: -4 }}
                transition={{ duration: 0.2 }}
                className="text-[10px] text-muted-foreground truncate"
              >
                {isProcessing ? processing.phaseLabel : meta.vendor}
              </motion.p>
            </AnimatePresence>
          </div>
        </div>

        {/* Center: animated processing status */}
        <div className="hidden md:flex items-center">
          <AnimatePresence mode="wait">
            {isProcessing ? (
              <motion.div
                key="processing"
                initial={{ opacity: 0, scale: 0.9 }}
                animate={{ opacity: 1, scale: 1 }}
                exit={{ opacity: 0, scale: 0.9 }}
                transition={{ duration: 0.3 }}
                className="flex items-center gap-3"
              >
                <ProgressRing progress={progress} />
                <div className="flex flex-col">
                  <span className="text-[11px] font-medium text-foreground">{progress}%</span>
                  <motion.div
                    className="h-[2px] w-16 rounded-full bg-secondary/40 overflow-hidden"
                  >
                    <motion.div
                      className="h-full rounded-full bg-primary"
                      animate={{ width: `${progress}%` }}
                      transition={{ duration: 0.3, ease: "easeOut" }}
                      style={{ boxShadow: "0 0 8px hsl(var(--primary) / 0.6)" }}
                    />
                  </motion.div>
                </div>
              </motion.div>
            ) : (
              <motion.div
                key="complete"
                initial={{ opacity: 0, scale: 0.9 }}
                animate={{ opacity: 1, scale: 1 }}
                exit={{ opacity: 0, scale: 0.9 }}
                transition={{ duration: 0.35, delay: 0.1 }}
                className="flex items-center gap-2 px-3 py-1.5 rounded-full border border-primary/20 bg-primary/5 backdrop-blur-sm"
              >
                <motion.div
                  initial={{ scale: 0 }}
                  animate={{ scale: 1 }}
                  transition={{ type: "spring", stiffness: 400, damping: 15, delay: 0.2 }}
                >
                  <CheckCircle2 className="h-3.5 w-3.5 text-primary" />
                </motion.div>
                <span className="text-[11px] font-medium text-primary">{meta.status}</span>
                <span className="w-px h-3 bg-primary/20" />
                <Shield className="h-3 w-3 text-primary/70" />
                <span className="text-[10px] text-primary/80">{meta.confidenceLevel}</span>
                <span className="w-px h-3 bg-primary/20" />
                <Sparkles className="h-3 w-3 text-primary/60" />
                <span className="text-[10px] text-primary/70">{meta.processingTime}</span>
              </motion.div>
            )}
          </AnimatePresence>
        </div>

       

              {/* Right: JSON export + layout + brand — shrink-0 avoids clipping on narrow viewports */}

              <div className="flex items-center gap-1.5 shrink-0">
                  {canExportJson && (
                      <>
                          <motion.button
                              type="button"
                              title="Download extraction JSON"
                              onClick={handleDownloadJson}
                              whileTap={{ scale: 0.9 }}
                              className="p-1.5 rounded-lg text-muted-foreground hover:text-foreground hover:bg-secondary/60 transition-colors"
                          >
                              <Download className="h-3.5 w-3.5" />
                          </motion.button>

                          <motion.button
                              type="button"
                              title="Copy extraction JSON"
                              onClick={handleCopyJson}
                              whileTap={{ scale: 0.9 }}
                              className="p-1.5 rounded-lg text-muted-foreground hover:text-foreground hover:bg-secondary/60 transition-colors"
                          >
                              {jsonCopied ? (
                                  <Check className="h-3.5 w-3.5 text-accent" />
                              ) : (
                                  <Copy className="h-3.5 w-3.5" />
                              )}
                          </motion.button>

                          <motion.button
                              type="button"
                              title={extractionXlsxButtonTitle(extractionResult?.document_type)}
                              onClick={handleDownloadXlsx}
                              whileTap={{ scale: 0.9 }}
                              className="p-1.5 rounded-lg text-muted-foreground hover:text-foreground hover:bg-secondary/60 transition-colors"
                          >
                              <FileSpreadsheet className="h-3.5 w-3.5" />
                          </motion.button>

                          <div className="w-px h-4 bg-border/30 mx-0.5" />
                      </>
                  )}

                  {onApprove && (
                      <>
                          <motion.button
                              type="button"
                              title="Approve extraction"
                              onClick={onApprove}
                              whileTap={{ scale: 0.9 }}
                              className="px-2.5 py-1.5 rounded-lg bg-primary text-primary-foreground hover:bg-primary/90 transition-colors"
                          >
                              Approve
                          </motion.button>
                          <div className="w-px h-4 bg-border/30 mx-0.5" />
                      </>
                  )}

                  {showWellTwinLink && (
                      <>
                          <motion.button
                              type="button"
                              title="Open visual well twin (trajectory, casing, risks)"
                              onClick={() => navigate("/well-twin")}
                              whileTap={{ scale: 0.97 }}
                              className="flex items-center gap-1.5 px-2 py-1 rounded-lg text-muted-foreground hover:text-accent hover:bg-accent/10 border border-transparent hover:border-accent/20 transition-colors"
                          >
                              <Layers className="h-3.5 w-3.5 shrink-0" />
                              <span className="text-[10px] font-medium">Well twin</span>
                          </motion.button>
                          <div className="w-px h-4 bg-border/30 mx-0.5" />
                      </>
                  )}

                  {onUploadNew && (
                      <>
                          <input
                              ref={uploadInputRef}
                              type="file"
                              accept="application/pdf,.pdf"
                              multiple={false}
                              className="hidden"
                              onChange={(e) => {
                                  const f = e.target.files?.[0];
                                  if (f) {
                                      const isPdf =
                                          f.type === "application/pdf" ||
                                          f.name.toLowerCase().endsWith(".pdf");
                                      if (!isPdf) {
                                          alert("Only a single PDF document is allowed.");
                                      } else {
                                          onUploadNew(f);
                                      }
                                  }
                                  e.target.value = "";
                              }}
                          />
                          <motion.button
                              type="button"
                              onClick={() => uploadInputRef.current?.click()}
                              whileTap={{ scale: 0.9 }}
                              title="Upload another PDF (same document type as current)"
                              className="p-1.5 rounded-lg text-muted-foreground hover:text-foreground hover:bg-secondary/60 transition-colors"
                          >
                              <Upload className="h-3.5 w-3.5" />
                          </motion.button>
                          <div className="w-px h-4 bg-border/30 mx-0.5" />
                      </>
                  )}

                  <motion.button
                      onClick={onTogglePdfExpand}
                      whileTap={{ scale: 0.9 }}
                      className={`p-1.5 rounded-lg transition-colors ${pdfExpanded
                              ? "bg-primary/15 text-primary"
                              : "text-muted-foreground hover:text-foreground hover:bg-secondary/60"
                          }`}
                      title={pdfExpanded ? "Exit fullscreen (⌘M)" : "Expand PDF (⌘M)"}
                  >
                      {pdfExpanded ? (
                          <Minimize2 className="h-3.5 w-3.5" />
                      ) : (
                          <Maximize2 className="h-3.5 w-3.5" />
                      )}
                  </motion.button>

                  <motion.button
                      onClick={onToggleAi}
                      whileTap={{ scale: 0.9 }}
                      className={`p-1.5 rounded-lg transition-colors ${aiVisible
                              ? "bg-primary/15 text-primary"
                              : "text-muted-foreground hover:text-foreground hover:bg-secondary/60"
                          }`}
                      title={aiVisible ? "Hide AI panel (⌘\\)" : "Show AI panel (⌘\\)"}
                  >
                      {aiVisible ? (
                          <PanelRightClose className="h-3.5 w-3.5" />
                      ) : (
                          <PanelRightOpen className="h-3.5 w-3.5" />
                      )}
                  </motion.button>

                  <div className="w-px h-4 bg-border/30 mx-0.5" />

                  <div className="flex items-center gap-1.5 px-2.5 py-1 rounded-full bg-secondary/30 border border-border/30">
                      <div
                          className="w-1.5 h-1.5 rounded-full"
                          style={{
                              background: "hsl(var(--primary))",
                              boxShadow: "0 0 6px hsl(var(--primary) / 0.6)",
                          }}
                      />
                      <span className="text-[10px] font-medium text-muted-foreground">
                          DocIQ
                      </span>
                  </div>
              </div>
          </div>


      {/* Bottom edge glow line */}
      {isProcessing && (
        <motion.div
          initial={{ opacity: 0 }}
          animate={{ opacity: 1 }}
          exit={{ opacity: 0 }}
          className="h-[1px] relative overflow-hidden"
        >
          <motion.div
            className="absolute inset-y-0 w-1/3"
            animate={{ left: ["-33%", "100%"] }}
            transition={{ duration: 1.8, repeat: Infinity, ease: "easeInOut" }}
            style={{
              background: "linear-gradient(90deg, transparent, hsl(var(--primary) / 0.8), transparent)",
            }}
          />
        </motion.div>
      )}
    </motion.div>
  );
}
