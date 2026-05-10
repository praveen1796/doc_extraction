import { motion, AnimatePresence } from "framer-motion";
import { useState, useEffect } from "react";
import { getAllDocTypes } from "@/services/classifier";
import type { StagedFile } from "./types";

interface FileCardProps {
  file: StagedFile;
  index: number;
  onChangeType: (fileId: string, newType: string) => void;
  onRemove: (fileId: string) => void;
}

const typeColors: Record<string, { bg: string; text: string; border: string; pill: string }> = {
  invoice: { bg: "bg-primary/10", text: "text-primary", border: "border-primary/25", pill: "type-pill-invoice" },
  purchase_order: { bg: "bg-purple-500/10", text: "text-purple-400", border: "border-purple-500/25", pill: "type-pill-po" },
  timesheet: { bg: "bg-warning/10", text: "text-warning", border: "border-warning/25", pill: "type-pill-timesheet" },
  toursheet: { bg: "bg-accent/10", text: "text-accent", border: "border-accent/25", pill: "type-pill-tour" },
};

function formatSize(bytes: number): string {
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / (1024 * 1024)).toFixed(1)} MB`;
}

function ScanAnimation() {
  return (
    <div className="absolute inset-0 rounded-xl overflow-hidden pointer-events-none z-10">
      <motion.div
        initial={{ top: "0%" }}
        animate={{ top: ["0%", "100%", "0%"] }}
        transition={{ duration: 2, repeat: Infinity, ease: "linear" }}
        className="absolute left-0 right-0 h-[2px]"
        style={{
          background: "linear-gradient(90deg, transparent 0%, hsl(var(--primary) / 0.7) 50%, transparent 100%)",
          boxShadow: "0 0 16px 3px hsl(var(--primary) / 0.25)",
        }}
      />
    </div>
  );
}

function ClassifyingShimmer() {
  return (
    <div className="flex items-center gap-2">
      <div className="h-5 w-20 rounded-full bg-secondary/60 shimmer-loading" />
      <div className="h-3 w-3 rounded-full border-2 border-primary/40 border-t-primary animate-spin" />
    </div>
  );
}

export function FileCard({ file, index, onChangeType, onRemove }: FileCardProps) {
  const [showTypeDropdown, setShowTypeDropdown] = useState(false);
  const docTypes = getAllDocTypes();
  const activeType = file.typeOverride || file.classification?.type || "invoice";
  const colors = typeColors[activeType] || typeColors.invoice;
  const isProcessing = file.stage === "extracting" || file.stage === "classifying";
  const isDone = file.stage === "done";
  const isError = file.stage === "error";

  const displayName =
    file.typeOverride
      ? docTypes.find((d) => d.id === file.typeOverride)?.displayName || file.typeOverride
      : file.classification?.displayName || "Classifying…";

  return (
    <motion.div
      layout
      initial={{ opacity: 0, y: 12, scale: 0.97 }}
      animate={{ opacity: 1, y: 0, scale: 1 }}
      exit={{ opacity: 0, scale: 0.95, y: -8 }}
      transition={{ duration: 0.35, delay: index * 0.06 }}
      className={`
        relative glass-panel transition-all duration-300 group
        ${isProcessing ? "border-primary/30 shadow-[0_0_20px_-6px_hsl(var(--primary)/0.2)]" : ""}
        ${isDone ? "border-accent/30" : ""}
        ${isError ? "border-destructive/30" : ""}
      `}
    >
      {/* Scanning beam during extraction */}
      {file.stage === "extracting" && <ScanAnimation />}

      <div className="p-4 flex items-start gap-4">
        {/* File icon / preview */}
        <div className={`
          relative flex-shrink-0 w-12 h-14 rounded-lg flex items-center justify-center overflow-hidden
          ${colors.bg} border ${colors.border} transition-all duration-300
        `}>
          {file.previewUrl ? (
            <img src={file.previewUrl} alt="" className="w-full h-full object-cover opacity-80" />
          ) : (
            <svg width="22" height="22" viewBox="0 0 24 24" fill="none" strokeWidth="1.5" strokeLinecap="round" className={colors.text}>
              <path d="M14 2H6a2 2 0 00-2 2v16a2 2 0 002 2h12a2 2 0 002-2V8z" stroke="currentColor" />
              <polyline points="14 2 14 8 20 8" stroke="currentColor" />
            </svg>
          )}
          {isDone && (
            <motion.div
              initial={{ scale: 0 }}
              animate={{ scale: 1 }}
              className="absolute inset-0 bg-accent/20 flex items-center justify-center"
            >
              <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="hsl(var(--accent))" strokeWidth="2.5" strokeLinecap="round">
                <polyline points="20 6 9 17 4 12" />
              </svg>
            </motion.div>
          )}
        </div>

        {/* File info */}
        <div className="flex-1 min-w-0">
          <div className="flex items-start justify-between gap-2 mb-1">
            <div className="min-w-0">
              <p className="text-sm font-semibold text-foreground truncate">{file.file.name}</p>
              <p className="text-xs text-muted-foreground mt-0.5">
                {formatSize(file.file.size)}
                {file.classification && file.stage !== "classifying" && (
                  <span className="ml-2 text-muted-foreground/50">
                    {Math.round(file.classification.confidence * 100)}% match
                  </span>
                )}
              </p>
            </div>

            {/* Remove button */}
            {file.stage === "ready" && (
              <button
                onClick={() => onRemove(file.id)}
                className="p-1 rounded-md opacity-0 group-hover:opacity-100 hover:bg-destructive/10 text-muted-foreground hover:text-destructive transition-all"
              >
                <svg width="14" height="14" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round">
                  <line x1="18" y1="6" x2="6" y2="18" /><line x1="6" y1="6" x2="18" y2="18" />
                </svg>
              </button>
            )}
          </div>

          {/* Classification badge / dropdown */}
          <div className="flex items-center gap-2 mt-2">
            {file.stage === "classifying" ? (
              <ClassifyingShimmer />
            ) : (
              <div className="relative">
                <button
                  onClick={() => file.stage === "ready" && setShowTypeDropdown(!showTypeDropdown)}
                  disabled={file.stage !== "ready"}
                  className={`
                    ${colors.pill || "type-pill-invoice"}
                    ${file.stage === "ready" ? "cursor-pointer hover:brightness-110" : "cursor-default"}
                    transition-all duration-200
                  `}
                >
                  {displayName}
                  {file.stage === "ready" && (
                    <svg width="10" height="10" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" className="ml-1">
                      <polyline points="6 9 12 15 18 9" />
                    </svg>
                  )}
                </button>

                {/* Type dropdown */}
                <AnimatePresence>
                  {showTypeDropdown && (
                    <>
                      <div className="fixed inset-0 z-40" onClick={() => setShowTypeDropdown(false)} />
                      <motion.div
                        initial={{ opacity: 0, y: 4, scale: 0.97 }}
                        animate={{ opacity: 1, y: 0, scale: 1 }}
                        exit={{ opacity: 0, y: 4, scale: 0.97 }}
                        className="absolute left-0 top-full mt-1 z-50 glass-panel p-1 w-48"
                      >
                        {docTypes.map((dt) => (
                          <button
                            key={dt.id}
                            onClick={() => {
                              onChangeType(file.id, dt.id);
                              setShowTypeDropdown(false);
                            }}
                            className={`
                              flex w-full items-center gap-2.5 rounded-lg px-3 py-2 text-xs font-medium transition-colors
                              ${activeType === dt.id ? "bg-primary/10 text-primary" : "text-secondary-foreground hover:bg-primary/5 hover:text-primary"}
                            `}
                          >
                            <span
                              className={`w-2 h-2 rounded-full ${
                                typeColors[dt.id]?.text.replace("text-", "bg-") || "bg-primary"
                              }`}
                            />
                            {dt.displayName}
                            <span className="text-muted-foreground/50 ml-auto text-[10px]">{dt.category}</span>
                          </button>
                        ))}
                      </motion.div>
                    </>
                  )}
                </AnimatePresence>
              </div>
            )}

            {/* Low confidence warning */}
            {file.classification && file.classification.confidence < 0.6 && file.stage === "ready" && !file.typeOverride && (
              <motion.span
                initial={{ opacity: 0 }}
                animate={{ opacity: 1 }}
                className="text-[10px] text-warning flex items-center gap-1"
              >
                <svg width="10" height="10" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5">
                  <path d="M10.29 3.86L1.82 18a2 2 0 001.71 3h16.94a2 2 0 001.71-3L13.71 3.86a2 2 0 00-3.42 0z" />
                  <line x1="12" y1="9" x2="12" y2="13" /><line x1="12" y1="17" x2="12.01" y2="17" />
                </svg>
                Verify type
              </motion.span>
            )}
          </div>

          {/* Progress bar during extraction */}
          <AnimatePresence>
            {file.stage === "extracting" && (
              <motion.div
                initial={{ opacity: 0, height: 0 }}
                animate={{ opacity: 1, height: "auto" }}
                exit={{ opacity: 0, height: 0 }}
                className="mt-3"
              >
                <div className="flex items-center justify-between mb-1.5">
                  <span className="text-[11px] text-primary font-medium">{file.progressMessage || "Processing…"}</span>
                  <span className="text-[11px] text-muted-foreground font-mono">{file.progress}%</span>
                </div>
                <div className="h-1.5 rounded-full bg-secondary/50 overflow-hidden">
                  <motion.div
                    className="h-full rounded-full bg-primary"
                    initial={{ width: "0%" }}
                    animate={{ width: `${file.progress}%` }}
                    transition={{ duration: 0.3, ease: "easeOut" }}
                    style={{ boxShadow: "0 0 8px hsl(var(--primary) / 0.5)" }}
                  />
                </div>
              </motion.div>
            )}
          </AnimatePresence>

          {/* Error message */}
          {file.stage === "error" && file.error && (
            <motion.div
              initial={{ opacity: 0 }}
              animate={{ opacity: 1 }}
              className="mt-2 flex items-start gap-2 text-xs text-destructive bg-destructive/5 border border-destructive/20 rounded-lg px-3 py-2"
            >
              <svg width="12" height="12" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" className="mt-0.5 flex-shrink-0">
                <circle cx="12" cy="12" r="10" /><line x1="15" y1="9" x2="9" y2="15" /><line x1="9" y1="9" x2="15" y2="15" />
              </svg>
              {file.error}
            </motion.div>
          )}
        </div>
      </div>
    </motion.div>
  );
}
