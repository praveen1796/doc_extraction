import { motion, AnimatePresence } from "framer-motion";
import { useState, useCallback, useRef, useEffect } from "react";
import { useNavigate } from "react-router-dom";
import {
  Upload as UploadIcon, FileText, ChevronLeft, Play,
  Shield, Layers, AlertTriangle, Loader2, Settings,
  Receipt, Clock, Clipboard, Target, ShoppingCart,
  Truck, HardHat, Wrench, FileSpreadsheet, Brain,
  Zap, Eye,
} from "lucide-react";
import {
  fetchDocumentTypes, isDemoMode, getDemoDocumentTypes,
  type DocumentTypeSummary,
} from "@/services/api";
import { useExtractionResult } from "@/hooks/useExtractionResult";

type Phase = "idle" | "activating" | "transitioning";

const ACCEPTED_TYPES = ["application/pdf", "image/png", "image/jpeg", "image/tiff"];

// ═══════════════════════════════════════════════════════════════════
//  Icon Registry — same as Studio
// ═══════════════════════════════════════════════════════════════════

const ICON_MAP: Record<string, React.ReactNode> = {
  "receipt": <Receipt className="h-6 w-6" />,
  "clock": <Clock className="h-6 w-6" />,
  "clipboard": <Clipboard className="h-6 w-6" />,
  "target": <Target className="h-6 w-6" />,
  "shopping-cart": <ShoppingCart className="h-6 w-6" />,
  "truck": <Truck className="h-6 w-6" />,
  "hard-hat": <HardHat className="h-6 w-6" />,
  "wrench": <Wrench className="h-6 w-6" />,
  "file-text": <FileText className="h-6 w-6" />,
  "file-spreadsheet": <FileSpreadsheet className="h-6 w-6" />,
  "layers": <Layers className="h-6 w-6" />,
  "brain": <Brain className="h-6 w-6" />,
  "zap": <Zap className="h-6 w-6" />,
  "settings": <Settings className="h-6 w-6" />,
};

// ═══════════════════════════════════════════════════════════════════
//  Category color map
// ═══════════════════════════════════════════════════════════════════

const CATEGORY_COLORS: Record<string, { bg: string; border: string; text: string; glow: string }> = {
  "Finance": { bg: "bg-primary/10", border: "border-primary/30", text: "text-primary", glow: "hover:shadow-[0_0_20px_-4px_hsl(192_95%_55%/0.3)]" },
  "Procurement": { bg: "bg-violet-500/10", border: "border-violet-500/30", text: "text-violet-400", glow: "hover:shadow-[0_0_20px_-4px_hsl(270_60%_55%/0.3)]" },
  "HR": { bg: "bg-warning/10", border: "border-warning/30", text: "text-warning", glow: "hover:shadow-[0_0_20px_-4px_hsl(38_92%_55%/0.3)]" },
  "Operations": { bg: "bg-accent/10", border: "border-accent/30", text: "text-accent", glow: "hover:shadow-[0_0_20px_-4px_hsl(160_70%_45%/0.3)]" },
  "Field Operations": { bg: "bg-emerald-500/10", border: "border-emerald-500/30", text: "text-emerald-400", glow: "hover:shadow-[0_0_20px_-4px_hsl(160_80%_40%/0.3)]" },
  "Safety": { bg: "bg-destructive/10", border: "border-destructive/30", text: "text-destructive", glow: "hover:shadow-[0_0_20px_-4px_hsl(0_72%_55%/0.3)]" },
};

const getColors = (category?: string) =>
  CATEGORY_COLORS[category || ""] || { bg: "bg-primary/10", border: "border-primary/30", text: "text-primary", glow: "" };

// ═══════════════════════════════════════════════════════════════════
//  Main Upload Component — Fully Dynamic
// ═══════════════════════════════════════════════════════════════════

export default function Upload() {
  const navigate = useNavigate();
  const { stageFile, file: stagedFile, documentType: stagedType } = useExtractionResult();

  // ── State ──
  const [docTypes, setDocTypes] = useState<DocumentTypeSummary[]>([]);
  const [loadingTypes, setLoadingTypes] = useState(true);
  const [phase, setPhase] = useState<Phase>("idle");
  const [selectedType, setSelectedType] = useState<string | null>(null);
  const [isDragOver, setIsDragOver] = useState(false);
  const [visibleSteps, setVisibleSteps] = useState(0);
  const [fileName, setFileName] = useState("");
  const [docTypeLabel, setDocTypeLabel] = useState("");
  const inputRef = useRef<HTMLInputElement>(null);
  const dragCounter = useRef(0);
  const navigated = useRef(false);

  const selected = docTypes.find(t => t.key === selectedType);

  // ── Load document types from API ──
  useEffect(() => {
    (async () => {
      setLoadingTypes(true);
      try {
        const types = isDemoMode() ? getDemoDocumentTypes() : await fetchDocumentTypes();
        setDocTypes(types.filter(t => t.enabled));
      } catch (err) {
        console.error("Failed to load document types:", err);
        setDocTypes(getDemoDocumentTypes().filter(t => t.enabled));
      }
      setLoadingTypes(false);
    })();
  }, []);

  // ── Auto-run pipeline when arriving with a pre-staged file (re-upload from workspace) ──
  useEffect(() => {
    if (phase !== "idle" || !stagedFile || !stagedType) return;
    setSelectedType(stagedType);
    setFileName(stagedFile.name);
    // Defer label resolution until docTypes load
    const dt = docTypes.find(t => t.key === stagedType);
    if (dt) setDocTypeLabel(dt.display_name);
    else setDocTypeLabel(stagedType);
    setPhase("activating");
  }, [stagedFile, stagedType, docTypes, phase]);

  // ── File processing (now uses dynamic types) ──
  const processFile = useCallback(
    (file: File) => {
      if (phase !== "idle" || !selectedType) return;

      const dt = docTypes.find(t => t.key === selectedType);
      console.log("[DocIQ Upload] Processing:", file.name, "as:", selectedType);
      setFileName(file.name);
      setDocTypeLabel(dt?.display_name || selectedType);
      stageFile(file, selectedType, false);
      setPhase("activating");
    },
    [phase, stageFile, selectedType, docTypes]
  );

  // ── Drag/drop handlers ──
  const onDragEnter = (e: React.DragEvent) => { e.preventDefault(); dragCounter.current++; setIsDragOver(true); };
  const onDragLeave = (e: React.DragEvent) => { e.preventDefault(); dragCounter.current--; if (dragCounter.current === 0) setIsDragOver(false); };
  const onDrop = useCallback((e: React.DragEvent) => {
    e.preventDefault(); setIsDragOver(false); dragCounter.current = 0;
    const file = e.dataTransfer.files[0];
    if (file && ACCEPTED_TYPES.includes(file.type)) processFile(file);
  }, [processFile]);
  const onFileChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0];
    if (file) processFile(file);
    e.target.value = "";
  };

  // ── Activation animation ──
  useEffect(() => {
    if (phase !== "activating") return;
    const steps = selected
      ? ["Upload", "Extract", "Validate", "Analyze"]
      : ["Upload", "Extract", "Validate", "Analyze"];
    let i = 0;
    const interval = setInterval(() => {
      i++;
      setVisibleSteps(i);
      if (i >= steps.length) {
        clearInterval(interval);
        setTimeout(() => setPhase("transitioning"), 400);
      }
    }, 400);
    return () => clearInterval(interval);
  }, [phase, selected]);

  // ── Navigate after activation ──
  useEffect(() => {
    if (phase !== "transitioning" || navigated.current) return;
    navigated.current = true;
    const timeout = setTimeout(() => {
      // Well plan uses same workspace as other types so WellPlanView + JSON export (TopActionBar) apply.
      // Use /well-twin for the full-screen twin experience from dashboard links if added later.
      navigate("/workspace");
    }, 600);
    return () => clearTimeout(timeout);
  }, [phase, navigate, selectedType]);

  // ── Reset ──
  useEffect(() => {
    navigated.current = false;
    setVisibleSteps(0);
  }, []);

  // ═══════════════════════════════════════════════════════════════════
  //  RENDER
  // ═══════════════════════════════════════════════════════════════════

  return (
    <div className="min-h-screen bg-background flex flex-col">
      {/* Top bar */}
      <div className="flex items-center justify-between px-6 py-4 border-b border-border/30">
        <div className="flex items-center gap-3">
          <button onClick={() => navigate("/")}
            className="p-2 rounded-lg border border-border/50 hover:bg-secondary/50 transition-colors">
            <ChevronLeft className="h-4 w-4 text-muted-foreground" />
          </button>
          <div>
            <h1 className="text-lg font-semibold text-foreground">Upload & Extract</h1>
            <p className="text-xs text-muted-foreground">Select document type, then upload</p>
          </div>
        </div>
        <button onClick={() => navigate("/studio")}
          className="flex items-center gap-2 px-3 py-2 rounded-lg border border-border/50 text-muted-foreground hover:text-foreground hover:bg-secondary/50 transition-colors text-sm">
          <Settings className="h-4 w-4" /> Configure Types
        </button>
      </div>

      <div className="flex-1 flex flex-col items-center justify-center px-6 py-8">
        <AnimatePresence mode="wait">
          {phase === "idle" && (
            <motion.div key="idle"
              initial={{ opacity: 0, y: 20 }} animate={{ opacity: 1, y: 0 }} exit={{ opacity: 0, scale: 0.98 }}
              className="w-full max-w-[900px] space-y-6"
            >
              {/* ── Step 1: Select Document Type ── */}
              <div>
                <h2 className="text-sm font-medium text-muted-foreground mb-3 flex items-center gap-2">
                  <span className="flex items-center justify-center h-5 w-5 rounded-full bg-primary/20 text-primary text-xs font-bold">1</span>
                  Select Document Type
                </h2>

                {loadingTypes ? (
                  <div className="flex items-center justify-center py-12">
                    <Loader2 className="h-5 w-5 animate-spin text-primary" />
                    <span className="ml-2 text-sm text-muted-foreground">Loading document types...</span>
                  </div>
                ) : (
                  <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 gap-3">
                    {docTypes.map((dt, i) => {
                      const colors = getColors(dt.category);
                      const isSelected = selectedType === dt.key;
                      return (
                        <motion.button key={dt.key}
                          initial={{ opacity: 0, y: 8 }}
                          animate={{ opacity: 1, y: 0 }}
                          transition={{ delay: i * 0.04 }}
                          onClick={() => setSelectedType(isSelected ? null : dt.key)}
                          className={`relative text-left p-4 rounded-xl border transition-all duration-200 ${colors.glow} ${
                            isSelected
                              ? `${colors.bg} ${colors.border} ring-2 ring-offset-1 ring-offset-background ${colors.border.replace("border-", "ring-")}`
                              : "glass-panel hover:border-border"
                          }`}
                        >
                          <div className="flex items-center gap-3 mb-2">
                            <div className={`p-2 rounded-xl ${colors.bg} ${colors.text} border ${colors.border}`}>
                              {ICON_MAP[dt.icon_name || "file-text"] || <FileText className="h-6 w-6" />}
                            </div>
                            <div className="flex-1 min-w-0">
                              <p className="font-semibold text-foreground text-sm truncate">{dt.display_name}</p>
                              <p className="text-xs text-muted-foreground">{dt.category}</p>
                            </div>
                            {isSelected && (
                              <motion.div initial={{ scale: 0 }} animate={{ scale: 1 }}
                                className={`flex items-center justify-center h-5 w-5 rounded-full ${colors.bg} ${colors.text}`}>
                                <Eye className="h-3 w-3" />
                              </motion.div>
                            )}
                          </div>
                          <p className="text-xs text-muted-foreground/70 line-clamp-2">{dt.description}</p>

                          {/* Feature pills */}
                          {isSelected && (
                            <motion.div initial={{ opacity: 0, height: 0 }} animate={{ opacity: 1, height: "auto" }}
                              className="mt-3 pt-3 border-t border-border/30"
                            >
                              <div className="flex flex-wrap gap-1.5">
                                {dt.sample_fields.slice(0, 4).map(f => (
                                  <span key={f} className="px-2 py-0.5 rounded-md bg-secondary/60 text-[10px] text-secondary-foreground border border-border/50">
                                    {f}
                                  </span>
                                ))}
                                {dt.dual_pass_enabled && (
                                  <span className={`px-2 py-0.5 rounded-md ${colors.bg} text-[10px] ${colors.text} border ${colors.border}`}>
                                    <Shield className="inline h-2.5 w-2.5 mr-0.5" />dual-pass
                                  </span>
                                )}
                              </div>
                            </motion.div>
                          )}
                        </motion.button>
                      );
                    })}
                  </div>
                )}
              </div>

              {/* ── Step 2: Upload Zone ── */}
              <AnimatePresence>
                {selectedType && (
                  <motion.div initial={{ opacity: 0, y: 12 }} animate={{ opacity: 1, y: 0 }} exit={{ opacity: 0 }}>
                    <h2 className="text-sm font-medium text-muted-foreground mb-3 flex items-center gap-2">
                      <span className="flex items-center justify-center h-5 w-5 rounded-full bg-primary/20 text-primary text-xs font-bold">2</span>
                      Upload Document
                    </h2>

                    <div
                      onDragEnter={onDragEnter} onDragOver={e => e.preventDefault()} onDragLeave={onDragLeave} onDrop={onDrop}
                      onClick={() => inputRef.current?.click()}
                      className={`relative glass-panel p-12 cursor-pointer transition-all duration-300 flex flex-col items-center gap-4 ${
                        isDragOver ? "border-primary/50 bg-primary/5 scale-[1.01]" : "hover:border-primary/30"
                      }`}
                    >
                      <input ref={inputRef} type="file" className="hidden"
                        accept={selected?.accepted_file_types.map(e => e.startsWith(".") ? e : `.${e}`).join(",") || ".pdf"}
                        onChange={onFileChange}
                      />

                      <div className={`p-4 rounded-2xl transition-colors ${
                        isDragOver ? "bg-primary/15 text-primary" : "bg-secondary/50 text-muted-foreground"
                      }`}>
                        <UploadIcon className="h-10 w-10" />
                      </div>

                      <div className="text-center">
                        <p className="font-medium text-foreground">
                          {isDragOver ? "Drop to extract" : `Upload ${selected?.display_name || "document"}`}
                        </p>
                        <p className="text-xs text-muted-foreground mt-1">
                          {selected?.accepted_file_types.join(", ")} · up to {selected?.max_file_size_mb || 50}MB
                        </p>
                      </div>

                      {/* Corner accents when dragging */}
                      {isDragOver && (
                        <>
                          <div className="absolute top-3 left-3 w-6 h-6 border-t-2 border-l-2 border-primary/60 rounded-tl-lg" />
                          <div className="absolute top-3 right-3 w-6 h-6 border-t-2 border-r-2 border-primary/60 rounded-tr-lg" />
                          <div className="absolute bottom-3 left-3 w-6 h-6 border-b-2 border-l-2 border-primary/60 rounded-bl-lg" />
                          <div className="absolute bottom-3 right-3 w-6 h-6 border-b-2 border-r-2 border-primary/60 rounded-br-lg" />
                        </>
                      )}
                    </div>
                  </motion.div>
                )}
              </AnimatePresence>
            </motion.div>
          )}

          {/* ── Activation Phase ── */}
          {(phase === "activating" || phase === "transitioning") && (
            <motion.div key="activating"
              initial={{ opacity: 0, scale: 0.98 }} animate={{ opacity: 1, scale: 1 }}
              exit={{ opacity: 0, scale: 1.02 }}
              className="flex flex-col items-center gap-8"
            >
              {/* File info */}
              <div className="text-center">
                <div className="p-4 rounded-2xl bg-primary/10 text-primary mx-auto w-fit mb-4">
                  {ICON_MAP[selected?.icon_name || "file-text"] || <FileText className="h-10 w-10" />}
                </div>
                <p className="text-lg font-semibold text-foreground">{fileName}</p>
                <p className="text-sm text-muted-foreground">{docTypeLabel}</p>
              </div>

              {/* Processing steps */}
              <div className="flex items-center gap-3">
                {["Upload", "Extract", "Validate", "Analyze"].map((step, i) => (
                  <div key={step} className="flex items-center gap-3">
                    <motion.div
                      initial={{ opacity: 0, scale: 0.8 }}
                      animate={visibleSteps > i ? { opacity: 1, scale: 1 } : {}}
                      className={`flex items-center gap-2 px-4 py-2 rounded-xl transition-all ${
                        visibleSteps > i
                          ? "bg-primary/15 text-primary border border-primary/30"
                          : "bg-secondary/30 text-muted-foreground/50 border border-border/30"
                      }`}
                    >
                      {visibleSteps > i ? (
                        <Loader2 className="h-3.5 w-3.5 animate-spin" />
                      ) : (
                        <div className="h-3.5 w-3.5 rounded-full bg-muted-foreground/20" />
                      )}
                      <span className="text-sm font-medium">{step}</span>
                    </motion.div>
                    {i < 3 && (
                      <div className={`w-8 h-0.5 rounded-full transition-colors ${
                        visibleSteps > i ? "bg-primary/40" : "bg-border/30"
                      }`} />
                    )}
                  </div>
                ))}
              </div>

              {/* Shimmer bar */}
              <div className="w-64 h-1 rounded-full bg-secondary overflow-hidden">
                <motion.div
                  initial={{ width: "0%" }}
                  animate={{ width: `${(visibleSteps / 4) * 100}%` }}
                  transition={{ duration: 0.4 }}
                  className="h-full bg-gradient-to-r from-primary/60 to-primary rounded-full"
                />
              </div>
            </motion.div>
          )}
        </AnimatePresence>
      </div>
    </div>
  );
}
