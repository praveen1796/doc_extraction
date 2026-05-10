import { motion, AnimatePresence } from "framer-motion";
import { useCallback, useState, useRef } from "react";

interface DropZoneProps {
  onFilesAdded: (files: File[]) => void;
  disabled?: boolean;
  compact?: boolean;
}

const ACCEPTED = [
  "application/pdf",
  "image/png",
  "image/jpeg",
  "image/tiff",
];

export function DropZone({ onFilesAdded, disabled = false, compact = false }: DropZoneProps) {
  const [isDragOver, setIsDragOver] = useState(false);
  const inputRef = useRef<HTMLInputElement>(null);
  const dragCounter = useRef(0);

  const handleDragEnter = useCallback((e: React.DragEvent) => {
    e.preventDefault();
    e.stopPropagation();
    dragCounter.current++;
    if (e.dataTransfer.items?.length) setIsDragOver(true);
  }, []);

  const handleDragLeave = useCallback((e: React.DragEvent) => {
    e.preventDefault();
    e.stopPropagation();
    dragCounter.current--;
    if (dragCounter.current === 0) setIsDragOver(false);
  }, []);

  const handleDragOver = useCallback((e: React.DragEvent) => {
    e.preventDefault();
    e.stopPropagation();
  }, []);

  const processFiles = useCallback(
    (fileList: FileList | null) => {
      if (!fileList || disabled) return;
      const valid = Array.from(fileList).filter(
        (f) => ACCEPTED.includes(f.type) || f.name.toLowerCase().endsWith(".pdf") || f.name.toLowerCase().endsWith(".tiff")
      );
      if (valid.length > 0) onFilesAdded(valid);
    },
    [onFilesAdded, disabled]
  );

  const handleDrop = useCallback(
    (e: React.DragEvent) => {
      e.preventDefault();
      e.stopPropagation();
      dragCounter.current = 0;
      setIsDragOver(false);
      processFiles(e.dataTransfer.files);
    },
    [processFiles]
  );

  const handleClick = () => {
    if (!disabled) inputRef.current?.click();
  };

  if (compact) {
    return (
      <>
        <input
          ref={inputRef}
          type="file"
          multiple
          accept=".pdf,.png,.jpg,.jpeg,.tiff"
          className="hidden"
          onChange={(e) => {
            processFiles(e.target.files);
            e.target.value = "";
          }}
        />
        <motion.button
          whileHover={{ scale: 1.01 }}
          whileTap={{ scale: 0.99 }}
          onClick={handleClick}
          onDragEnter={handleDragEnter}
          onDragLeave={handleDragLeave}
          onDragOver={handleDragOver}
          onDrop={handleDrop}
          disabled={disabled}
          className={`
            w-full rounded-xl border-2 border-dashed transition-all duration-300 py-5 px-6
            flex items-center justify-center gap-3 cursor-pointer
            ${isDragOver
              ? "border-primary bg-primary/8 shadow-[0_0_30px_-8px_hsl(var(--primary)/0.3)]"
              : "border-border/60 bg-secondary/20 hover:border-primary/40 hover:bg-primary/5"
            }
            ${disabled ? "opacity-40 pointer-events-none" : ""}
          `}
        >
          <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" className="text-primary/70">
            <path d="M21 15v4a2 2 0 01-2 2H5a2 2 0 01-2-2v-4" />
            <polyline points="17 8 12 3 7 8" />
            <line x1="12" y1="3" x2="12" y2="15" />
          </svg>
          <span className="text-sm font-medium text-muted-foreground">
            {isDragOver ? "Drop files here" : "Add more files"}
          </span>
        </motion.button>
      </>
    );
  }

  return (
    <>
      <input
        ref={inputRef}
        type="file"
        multiple
        accept=".pdf,.png,.jpg,.jpeg,.tiff"
        className="hidden"
        onChange={(e) => {
          processFiles(e.target.files);
          e.target.value = "";
        }}
      />
      <motion.div
        initial={{ opacity: 0, y: 20 }}
        animate={{ opacity: 1, y: 0 }}
        transition={{ duration: 0.6, ease: "easeOut" }}
        onClick={handleClick}
        onDragEnter={handleDragEnter}
        onDragLeave={handleDragLeave}
        onDragOver={handleDragOver}
        onDrop={handleDrop}
        className={`
          relative overflow-hidden rounded-2xl border-2 border-dashed cursor-pointer
          transition-all duration-500 group
          ${isDragOver
            ? "border-primary bg-primary/6 shadow-[0_0_60px_-12px_hsl(var(--primary)/0.4)]"
            : "border-border/50 bg-muted/10 hover:border-primary/30 hover:bg-primary/3"
          }
          ${disabled ? "opacity-40 pointer-events-none" : ""}
          ${compact ? "py-8 px-6" : "py-20 px-8"}
        `}
      >
        {/* Scan beam animation on drag-over */}
        <AnimatePresence>
          {isDragOver && (
            <motion.div
              initial={{ top: "-2px" }}
              animate={{ top: ["0%", "100%", "0%"] }}
              transition={{ duration: 2.5, repeat: Infinity, ease: "linear" }}
              exit={{ opacity: 0 }}
              className="absolute left-0 right-0 h-[2px] z-10"
              style={{
                background:
                  "linear-gradient(90deg, transparent, hsl(var(--primary) / 0.8), transparent)",
                boxShadow: "0 0 20px 4px hsl(var(--primary) / 0.3)",
              }}
            />
          )}
        </AnimatePresence>

        {/* Grid lines background */}
        <div
          className="absolute inset-0 opacity-[0.03] pointer-events-none"
          style={{
            backgroundImage: `
              linear-gradient(hsl(var(--primary)) 1px, transparent 1px),
              linear-gradient(90deg, hsl(var(--primary)) 1px, transparent 1px)
            `,
            backgroundSize: "40px 40px",
          }}
        />

        {/* Corner brackets */}
        <div className="absolute top-4 left-4 w-8 h-8 border-t-2 border-l-2 border-primary/20 rounded-tl-lg transition-colors duration-300 group-hover:border-primary/40" />
        <div className="absolute top-4 right-4 w-8 h-8 border-t-2 border-r-2 border-primary/20 rounded-tr-lg transition-colors duration-300 group-hover:border-primary/40" />
        <div className="absolute bottom-4 left-4 w-8 h-8 border-b-2 border-l-2 border-primary/20 rounded-bl-lg transition-colors duration-300 group-hover:border-primary/40" />
        <div className="absolute bottom-4 right-4 w-8 h-8 border-b-2 border-r-2 border-primary/20 rounded-br-lg transition-colors duration-300 group-hover:border-primary/40" />

        <div className="relative z-10 flex flex-col items-center text-center">
          {/* Upload icon */}
          <motion.div
            animate={isDragOver ? { scale: 1.15, y: -4 } : { scale: 1, y: 0 }}
            transition={{ type: "spring", stiffness: 300, damping: 20 }}
            className="mb-6"
          >
            <div className={`
              w-20 h-20 rounded-2xl flex items-center justify-center transition-all duration-500
              ${isDragOver
                ? "bg-primary/15 shadow-[0_0_30px_-4px_hsl(var(--primary)/0.4)]"
                : "bg-secondary/60 group-hover:bg-primary/10"
              }
            `}>
              <svg width="32" height="32" viewBox="0 0 24 24" fill="none" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round"
                className={`transition-colors duration-300 ${isDragOver ? "stroke-primary" : "stroke-muted-foreground group-hover:stroke-primary/70"}`}>
                <path d="M14 2H6a2 2 0 00-2 2v16a2 2 0 002 2h12a2 2 0 002-2V8z" />
                <polyline points="14 2 14 8 20 8" />
                <line x1="12" y1="18" x2="12" y2="12" />
                <polyline points="9 15 12 12 15 15" />
              </svg>
            </div>
          </motion.div>

          <AnimatePresence mode="wait">
            {isDragOver ? (
              <motion.div
                key="drop"
                initial={{ opacity: 0, y: 4 }}
                animate={{ opacity: 1, y: 0 }}
                exit={{ opacity: 0, y: -4 }}
              >
                <p className="text-lg font-semibold text-primary mb-1">Release to upload</p>
                <p className="text-sm text-primary/60">Documents will be auto-classified</p>
              </motion.div>
            ) : (
              <motion.div
                key="idle"
                initial={{ opacity: 0, y: 4 }}
                animate={{ opacity: 1, y: 0 }}
                exit={{ opacity: 0, y: -4 }}
              >
                <p className="text-lg font-semibold text-foreground mb-1">
                  Drop documents here
                </p>
                <p className="text-sm text-muted-foreground mb-5">
                  or click to browse — PDF, PNG, JPEG, TIFF up to 50 MB
                </p>
                <div className="flex items-center justify-center gap-3 flex-wrap">
                  {[
                    { label: "Invoices", color: "primary" },
                    { label: "Purchase Orders", color: "purple-400" },
                    { label: "Tour Sheets", color: "accent" },
                    { label: "Timesheets", color: "warning" },
                  ].map((t) => (
                    <span
                      key={t.label}
                      className="inline-flex items-center gap-1.5 rounded-full px-3 py-1 text-xs font-medium bg-secondary/50 text-muted-foreground border border-border/30"
                    >
                      <span className={`w-1.5 h-1.5 rounded-full bg-${t.color}`} />
                      {t.label}
                    </span>
                  ))}
                </div>
              </motion.div>
            )}
          </AnimatePresence>
        </div>
      </motion.div>
    </>
  );
}
