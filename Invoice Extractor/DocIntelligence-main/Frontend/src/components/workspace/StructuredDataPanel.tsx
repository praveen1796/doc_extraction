import { motion, AnimatePresence } from "framer-motion";
import { useState } from "react";
import { AlertTriangle, CheckCircle2, Info, ChevronDown, ChevronRight, Loader2, Copy, Check, Code, Table2, Layers, Download, FileSpreadsheet, Pencil } from "lucide-react";
import { InvoiceEditor } from "@/components/workspace/InvoiceEditor";
import {
  buildFullExtractionExport,
  copyJsonToClipboard,
  downloadJson,
  downloadExtractionXlsx,
  extractionJsonBaseName,
  extractionXlsxButtonTitle,
} from "@/lib/extractionExport";
import type { MappedWorkspaceData, GenericSection, GenericTable, GenericField } from "@/services/dataMapper";
import type { ExtractionResponse } from "@/services/api";
import { WellPlanView } from "@/components/workspace/wellplan/WellPlanView";

//const closeEditor = () => {setEditorOpen(false);
//};


// ═══════════════════════════════════════════════════════════════════
//  Confidence Bar — reusable
// ═══════════════════════════════════════════════════════════════════

function ConfidenceBar({ value }: { value: number }) {
  const color = value >= 90 ? "bg-accent" : value >= 75 ? "bg-warning" : "bg-destructive";
    const glow = value >= 90 ? "accent" : value >= 75 ? "warning" : "destructive";
  return (
    <div className="flex items-center gap-2">
      <div className="w-10 h-1.5 rounded-full bg-secondary overflow-hidden">
        <motion.div
          initial={{ width: 0 }}
          animate={{ width: `${value}%` }}
          transition={{ duration: 0.6, ease: "easeOut" }}
          className={`h-full rounded-full ${color}`}
          style={{ boxShadow: `0 0 6px hsl(var(--${glow}) / 0.5)` }}
        />
      </div>
      <span className="text-[10px] text-muted-foreground w-7 text-right">{value}%</span>
    </div>
  );
}

// ═══════════════════════════════════════════════════════════════════
//  Field Row — single key-value pair with confidence
// ═══════════════════════════════════════════════════════════════════

function FieldRow({ field, revealed }: { field: GenericField; revealed: boolean }) {
  if (!revealed) return (
    <div className="flex items-center justify-between py-2.5 px-3 rounded-lg">
      <div className="space-y-1.5"><div className="h-3 w-16 rounded bg-secondary/60 shimmer-loading" /><div className="h-4 w-32 rounded bg-secondary/40 shimmer-loading" /></div>
      <div className="flex items-center gap-2 ml-3"><Loader2 className="h-3 w-3 text-primary/40 animate-spin" /><span className="text-[10px] text-primary/40">Extracting…</span></div>
    </div>
  );
  const isCurrency = field.type === "currency";
  return (
    <motion.div initial={{ opacity: 0, y: 4 }} animate={{ opacity: 1, y: 0 }} transition={{ duration: 0.35 }}
      className={`flex items-center justify-between py-2.5 px-3 rounded-lg hover:bg-secondary/40 transition-colors ${isCurrency ? "border border-border/30 bg-secondary/10" : ""}`}>
      <div className="min-w-0 flex-1">
        <p className="text-[10px] uppercase tracking-wider text-muted-foreground/60 mb-0.5">{field.label}</p>
        <p className={`text-sm truncate ${isCurrency ? "font-semibold text-foreground" : "text-foreground/90"}`}>{field.value}</p>
        {field.type === "date" && <p className="text-[10px] text-muted-foreground/40 mt-0.5">{field.key}</p>}
      </div>
      <div className="ml-3 flex-shrink-0"><ConfidenceBar value={field.confidence} /></div>
    </motion.div>
  );
}

// ═══════════════════════════════════════════════════════════════════
//  Section Header — collapsible
// ═══════════════════════════════════════════════════════════════════

function SectionHeader({ title, count, isOpen, onToggle, icon }: { title: string; count?: string; isOpen: boolean; onToggle: () => void; icon?: React.ReactNode }) {
  return (
    <button onClick={onToggle} className="flex items-center justify-between w-full px-1 py-1 group cursor-pointer">
      <div className="flex items-center gap-1.5">
        <motion.div animate={{ rotate: isOpen ? 90 : 0 }} transition={{ duration: 0.2 }}>
          <ChevronRight className="h-3.5 w-3.5 text-muted-foreground group-hover:text-foreground transition-colors" />
        </motion.div>
        {icon && <span className="text-muted-foreground">{icon}</span>}
        <h3 className="text-xs font-medium text-muted-foreground group-hover:text-foreground transition-colors">{title}</h3>
      </div>
      {count && <span className="text-[10px] text-muted-foreground/60">{count}</span>}
    </button>
  );
}

// ═══════════════════════════════════════════════════════════════════
//  Generic Table — auto-generated columns from JSON
// ═══════════════════════════════════════════════════════════════════

function GenericTableView({ table, isComplete }: { table: GenericTable; isComplete: boolean }) {
  const [open, setOpen] = useState(true);

  function fmtCell(val: unknown, type: string): string {
    if (val == null || val === "") return "—";
    if (type === "currency" && typeof val === "number")
      return "$" + val.toLocaleString("en-US", { minimumFractionDigits: 2, maximumFractionDigits: 2 });
    if (typeof val === "number") return val.toLocaleString();
    return String(val);
  }

  return (
    <div className="mb-3">
      <SectionHeader title={table.label} count={`${table.rows.length} rows`} isOpen={open} onToggle={() => setOpen(!open)} icon={<Table2 className="h-3 w-3" />} />
      <AnimatePresence>
        {open && (
          <motion.div initial={{ opacity: 0, height: 0 }} animate={{ opacity: 1, height: "auto" }} exit={{ opacity: 0, height: 0 }} className="overflow-hidden">
            <div className="overflow-auto max-h-[300px] custom-scrollbar rounded-lg border border-border/30 mt-1">
              <table className="w-full text-sm">
                <thead className="sticky top-0 z-10">
                  <tr className="bg-secondary/60 backdrop-blur-sm">
                    <th className="py-2 px-3 text-left text-[10px] uppercase tracking-wider text-muted-foreground font-medium">#</th>
                    {table.columns.map(col => (
                      <th key={col.key}
                        className={`py-2 px-3 text-[10px] uppercase tracking-wider text-muted-foreground font-medium ${col.type === "number" || col.type === "currency" ? "text-right" : "text-left"}`}>
                        {col.label}
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {table.rows.map((row, i) => (
                    <motion.tr key={i}
                      initial={isComplete ? {} : { opacity: 0, x: -8 }}
                      animate={{ opacity: 1, x: 0 }}
                      transition={{ duration: 0.3, delay: isComplete ? 0 : i * 0.05 }}
                      className="border-b border-border/20 hover:bg-secondary/30 transition-colors">
                      <td className="py-2 px-3 text-xs text-muted-foreground/50">{i + 1}</td>
                      {table.columns.map(col => (
                        <td key={col.key}
                          className={`py-2 px-3 text-sm ${col.type === "number" || col.type === "currency" ? "text-right font-medium" : ""} ${col.type === "currency" ? "text-foreground" : "text-foreground/80"}`}>
                          {fmtCell(row[col.key], col.type)}
                        </td>
                      ))}
                    </motion.tr>
                  ))}
                </tbody>
              </table>
            </div>
          </motion.div>
        )}
      </AnimatePresence>
    </div>
  );
}

// ═══════════════════════════════════════════════════════════════════
//  Main Panel
// ═══════════════════════════════════════════════════════════════════

interface Props {
  revealedFieldIds: Set<string>;
  revealedLineItemIds: Set<string>;
  isComplete: boolean;
  workspaceData?: MappedWorkspaceData | null;
  /** Full API response for JSON download / copy (optional) */
  extractionResult?: ExtractionResponse | null;
  /** When provided + document is an AP invoice, exposes the Edit button */
    onApproveInvoiceEdits?: (updatedData: Record<string, unknown>, hasEdits: boolean) => void;
  /** Optional: called right before opening the Edit modal so parent can collapse the AI panel */
    onBeforeEditOpen?: () => void;
    onEditorClose?: () => void;
}

export function StructuredDataPanel({ revealedFieldIds, revealedLineItemIds, isComplete, workspaceData, extractionResult, onApproveInvoiceEdits, onBeforeEditOpen, onEditorClose, }: Props) {
    // ── Well Plan: use specialized view ──
    console.log("StructuredDataPanel rendered");
    const [validationOpen, setValidationOpen] = useState(true);
    const [showRawJson, setShowRawJson] = useState(false);
    const [copied, setCopied] = useState(false);
    const [editorOpen, setEditorOpen] = useState(false);
    const [editorData, setEditorData] = useState<Record<string, unknown> | null>(null);
    const sections = workspaceData?.sections ?? [];
    const tables = workspaceData?.tables ?? [];
    const validation = workspaceData?.validationIssues ?? [];
    const isFieldRevealed = (id: string) => isComplete || revealedFieldIds.has(id);

    const [openSections, setOpenSections] = useState<Record<string, boolean>>({});
    const isSectionOpen = (key: string) => openSections[key] ?? true;
    const toggleSection = (key: string) => setOpenSections(prev => ({ ...prev, [key]: !isSectionOpen(key) }));
    const closeEditor = () => {
        setEditorOpen(false);
        onEditorClose?.(); 
    };

  if (workspaceData?.documentType === "well_plan") {
    return (
      <motion.div initial={{ opacity: 0, y: 12 }} animate={{ opacity: 1, y: 0 }} transition={{ duration: 0.4, delay: 0.2 }} className="glass-panel flex flex-col h-full overflow-hidden">
        <WellPlanView
          rawData={workspaceData.rawData}
          fileName={workspaceData.documentMeta.fileName}
          extractionResult={extractionResult}
        />
      </motion.div>
    );
  }

  // ── Generic: auto-render from sections + tables ──
  

 

   


  const docType = (extractionResult?.document_type ?? workspaceData?.documentType ?? "").toLowerCase();
  const isInvoice = docType === "invoice";
  const canEdit = isInvoice && !!onApproveInvoiceEdits && !!extractionResult?.data;

  const exportPayload = extractionResult
    ? buildFullExtractionExport(extractionResult)
    : workspaceData?.rawData;

  const copyJson = async () => {
    if (!exportPayload) return;
    try {
      await copyJsonToClipboard(exportPayload);
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    } catch {
      /* clipboard denied */
    }
  };

  const saveJson = () => {
    if (!exportPayload || !workspaceData) return;
    const stem = extractionJsonBaseName(workspaceData.documentMeta.fileName);
    downloadJson(`${stem}-${workspaceData.documentType}-extraction`, exportPayload);
  };

  const saveXlsx = () => {
    if (!extractionResult?.data || !workspaceData) return;
    downloadExtractionXlsx(extractionResult, workspaceData.documentMeta.fileName);
  };

  const totalFields = sections.reduce((s, sec) => s + sec.fields.length, 0);

  return (
    <motion.div initial={{ opacity: 0, y: 12 }} animate={{ opacity: 1, y: 0 }} transition={{ duration: 0.4, delay: 0.2 }}
      className="glass-panel flex flex-col h-full overflow-hidden">

      {/* Header strip */}
      <div className="flex items-center justify-between px-4 py-3 border-b border-border/30">
        <div className="flex items-center gap-2">
          <Layers className="h-4 w-4 text-primary" />
          <h2 className="text-sm font-semibold text-foreground">Extracted Data</h2>
          <span className="text-[10px] text-muted-foreground/60 ml-1">{totalFields} fields · {tables.reduce((s, t) => s + t.rows.length, 0)} rows</span>
        </div>
        <div className="flex items-center gap-1">
          {canEdit && (
            <button
              type="button"
              title="Edit invoice fields"
                          onClick={() => {
                              if (!extractionResult?.data) return;
                              onBeforeEditOpen?.();
                              setEditorData(extractionResult.data);
                              setEditorOpen(true);
                          }}
              className="p-1.5 rounded-md text-muted-foreground hover:text-primary hover:bg-primary/10 transition-colors"
            >
              <Pencil className="h-3.5 w-3.5" />
            </button>
          )}
          <button type="button" title="Download JSON" onClick={saveJson} disabled={!exportPayload}
            className="p-1.5 rounded-md text-muted-foreground hover:text-foreground hover:bg-secondary/50 transition-colors disabled:opacity-40">
            <Download className="h-3.5 w-3.5" />
          </button>
          <button
            type="button"
            title={extractionXlsxButtonTitle(extractionResult?.document_type)}
            onClick={saveXlsx}
            disabled={!extractionResult?.data}
            className="p-1.5 rounded-md text-muted-foreground hover:text-foreground hover:bg-secondary/50 transition-colors disabled:opacity-40">
            <FileSpreadsheet className="h-3.5 w-3.5" />
          </button>
          <button type="button" onClick={() => setShowRawJson(!showRawJson)}
            className={`p-1.5 rounded-md transition-colors ${showRawJson ? "bg-primary/15 text-primary" : "text-muted-foreground hover:text-foreground hover:bg-secondary/50"}`}>
            <Code className="h-3.5 w-3.5" />
          </button>
          <button type="button" title="Copy JSON" onClick={copyJson} disabled={!exportPayload}
            className="p-1.5 rounded-md text-muted-foreground hover:text-foreground hover:bg-secondary/50 transition-colors disabled:opacity-40">
            {copied ? <Check className="h-3.5 w-3.5 text-accent" /> : <Copy className="h-3.5 w-3.5" />}
          </button>
        </div>
      </div>

      <div className="flex-1 overflow-auto custom-scrollbar p-4 space-y-3">
        {/* Raw JSON view */}
        <AnimatePresence>
          {showRawJson && (
            <motion.div initial={{ opacity: 0, height: 0 }} animate={{ opacity: 1, height: "auto" }} exit={{ opacity: 0, height: 0 }} className="overflow-hidden">
              <pre className="p-3 rounded-lg bg-secondary/30 border border-border/50 text-[11px] text-foreground/80 overflow-auto max-h-[300px] font-mono custom-scrollbar mb-3">
                {JSON.stringify(exportPayload, null, 2)}
              </pre>
            </motion.div>
          )}
        </AnimatePresence>

        {/* Dynamic sections */}
        {sections.map(section => (
          <div key={section.id}>
            <SectionHeader
              title={section.label}
              count={`${section.fields.length} fields`}
              isOpen={isSectionOpen(section.key)}
              onToggle={() => toggleSection(section.key)}
            />
            <AnimatePresence>
              {isSectionOpen(section.key) && (
                <motion.div initial={{ opacity: 0, height: 0 }} animate={{ opacity: 1, height: "auto" }} exit={{ opacity: 0, height: 0 }} className="overflow-hidden">
                  <div className="space-y-0.5 mt-1">
                    {section.fields.map(field => (
                      <FieldRow key={field.id} field={field} revealed={isFieldRevealed(field.id)} />
                    ))}
                  </div>
                </motion.div>
              )}
            </AnimatePresence>
          </div>
        ))}

        {/* Dynamic tables */}
        {tables.map(table => (
          <GenericTableView key={table.id} table={table} isComplete={isComplete} />
        ))}

        {/* Validation */}
        {validation.length > 0 && (
          <div>
            <SectionHeader title="Validation" count={`${validation.length} issues`} isOpen={validationOpen} onToggle={() => setValidationOpen(!validationOpen)} />
            <AnimatePresence>
              {validationOpen && (
                <motion.div initial={{ opacity: 0, height: 0 }} animate={{ opacity: 1, height: "auto" }} exit={{ opacity: 0, height: 0 }} className="overflow-hidden">
                  <div className="space-y-1.5 mt-1">
                    {validation.map(issue => (
                      <motion.div key={issue.id} initial={{ opacity: 0, x: -6 }} animate={{ opacity: 1, x: 0 }} className={`flex items-start gap-2 px-3 py-2.5 rounded-lg border ${
                        issue.type === "error" ? "bg-destructive/5 border-destructive/20" : issue.type === "warning" ? "bg-warning/5 border-warning/20" : "bg-primary/5 border-primary/20"
                      }`}>
                        {issue.type === "error" ? <AlertTriangle className="h-3.5 w-3.5 text-destructive flex-shrink-0 mt-0.5" />
                          : issue.type === "warning" ? <AlertTriangle className="h-3.5 w-3.5 text-warning flex-shrink-0 mt-0.5" />
                          : <Info className="h-3.5 w-3.5 text-primary flex-shrink-0 mt-0.5" />}
                        <div>
                          <p className="text-xs font-medium">{issue.label}</p>
                          <p className="text-[11px] text-muted-foreground mt-0.5">{issue.detail}</p>
                        </div>
                      </motion.div>
                    ))}
                  </div>
                </motion.div>
              )}
            </AnimatePresence>
          </div>
        )}

        {/* Empty state */}
        {sections.length === 0 && tables.length === 0 && !isComplete && (
          <div className="flex flex-col items-center justify-center py-12 text-muted-foreground/50">
            <Loader2 className="h-8 w-8 animate-spin mb-3" />
            <p className="text-sm">Extracting document data...</p>
          </div>
        )}
      </div>

          {canEdit && editorOpen && editorData && (
              <InvoiceEditor
                  open={editorOpen}
                  data={editorData}
                  onApprove={(data, hasEdits) =>
                      onApproveInvoiceEdits(data, hasEdits)
                  }
                  onClose={closeEditor}
              />
          )}
    </motion.div>
  );
}