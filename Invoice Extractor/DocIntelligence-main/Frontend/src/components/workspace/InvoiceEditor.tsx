import { useEffect, useMemo, useRef, useState } from "react";
import { motion, AnimatePresence } from "framer-motion";
import { X, Check, Plus, Trash2, FileEdit, RotateCcw, Undo2, ListOrdered, AlertTriangle } from "lucide-react";
import { Button } from "@/components/ui/button";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { Textarea } from "@/components/ui/textarea";

// ─────────────────────────────────────────────────────────────────────
// Invoice Editor — clean form UI for editing the extracted JSON.
// Only used when document_type === "invoice" (AP Invoice).
// On Approve, calls onApprove(updatedData) so parent updates the
// raw extraction (JSON view, exports, AI context all stay in sync).
// ─────────────────────────────────────────────────────────────────────

type Json = Record<string, unknown>;


interface Props {
    open: boolean;
    onClose: () => void;
    data: Json;
    onApprove: (next: Json, hasEdits: boolean) => void;
}


// Keys that shouldn't be exposed in the editor (metadata-ish embedded fields)
const HIDDEN_KEYS = new Set([
  "confidence",
  "document_type",
  "language",
  "extraction_version",
  "model_confidence",
  "processing_notes",
]);

function humanize(key: string): string {
  return key
    .replace(/_/g, " ")
    .replace(/([a-z])([A-Z])/g, "$1 $2")
    .replace(/\b\w/g, (c) => c.toUpperCase());
}

function isNumericKey(key: string): boolean {
  return /amount|total|subtotal|price|cost|fee|tax|discount|balance|payment|charge|qty|quantity|rate|count/i.test(
    key
  );
}

function deepClone<T>(v: T): T {
  return JSON.parse(JSON.stringify(v)) as T;
}

// Coerce string back to number/boolean/null when round-tripping
function coerce(original: unknown, next: string): unknown {
  if (typeof original === "number") {
    if (next.trim() === "") return null;
    const n = Number(next);
    return Number.isFinite(n) ? n : next;
  }
  if (typeof original === "boolean") return next === "true";
  if (next === "") return null;
  return next;
}

// Pick column-span (1–4) so the field's box visually fits its content length.
// Returns Tailwind classes that work on a 4-col grid (collapses gracefully on smaller grids).
function spanForValue(val: unknown, key: string): string {
  const str = val == null ? "" : String(val);
  const len = Math.max(str.length, humanize(key).length);
  if (len > 80) return "col-span-1 sm:col-span-2 lg:col-span-3 xl:col-span-4";
  if (len > 40) return "col-span-1 sm:col-span-2 lg:col-span-2 xl:col-span-2";
  if (len > 20) return "col-span-1 sm:col-span-2 lg:col-span-2 xl:col-span-2";
  return "col-span-1";
}

// Render either an <Input> or a <Textarea> depending on content length.
function DynamicField({
  fieldKey,
  value,
  onChange,
}: {
  fieldKey: string;
  value: unknown;
  onChange: (raw: string) => void;
}) {
  const str = value == null ? "" : String(value);
  const isLong = str.length > 60;
  const isNumber = isNumericKey(fieldKey) || typeof value === "number";

  return (
    <div className={`space-y-1 min-w-0 ${spanForValue(value, fieldKey)}`}>
      <Label className="text-[11px] text-muted-foreground">
        {humanize(fieldKey)}
      </Label>
      {isLong && !isNumber ? (
        <Textarea
          value={str}
          onChange={(e) => onChange(e.target.value)}
          rows={Math.min(6, Math.max(2, Math.ceil(str.length / 60)))}
          className="text-sm resize-y min-h-[60px]"
        />
      ) : (
        <Input
          type={isNumber ? "number" : "text"}
          step="any"
          value={str}
          onChange={(e) => onChange(e.target.value)}
          className="h-9 text-sm w-full"
        />
      )}
    </div>
  );
}

export function InvoiceEditor({ open, onClose, data, onApprove }: Props) {
    const [draft, setDraft] = useState<Json>(() => deepClone(data));
    const originalRef = useRef<Json>(deepClone(data));
  // History stack of previous drafts — used by the Undo button to revert one step at a time.
    const [history, setHistory] = useState<Json[]>([]);
   
  // Guard so the open-effect reset doesn't push to history.
  const skipNextHistoryRef = useRef(false);

  // Re-seed draft each time we open with fresh data
  useEffect(() => {
    if (open) {
      skipNextHistoryRef.current = true;
      setDraft(deepClone(data));
        setHistory([]);
        originalRef.current = deepClone(data);
    }
  }, [open, data]);

  // Push current draft into history before applying an update.
  const commit = (updater: (prev: Json) => Json) => {
    setDraft((prev) => {
      setHistory((h) => [...h, deepClone(prev)]);
      return updater(prev);
    });
  };

  const undo = () => {
    setHistory((h) => {
      if (h.length === 0) return h;
      const next = [...h];
      const last = next.pop()!;
      setDraft(last);
      return next;
    });
  };

  // Partition keys: scalars vs arrays-of-objects (line items)
  const { scalarKeys, tableKeys } = useMemo(() => {
    const s: string[] = [];
    const t: string[] = [];
    for (const [k, v] of Object.entries(draft)) {
      if (HIDDEN_KEYS.has(k)) continue;
      if (
        Array.isArray(v) &&
        v.length > 0 &&
        typeof v[0] === "object" &&
        v[0] !== null
      ) {
        t.push(k);
      } else if (typeof v !== "object" || v === null) {
        s.push(k);
      } else if (Array.isArray(v)) {
        // empty array — treat as table with no rows
        t.push(k);
      } else {
        // nested object — flatten one level into scalar editors via dotted keys
        s.push(k);
      }
    }
    return { scalarKeys: s, tableKeys: t };
  }, [draft]);

  const updateScalar = (key: string, raw: string) => {
    commit((prev) => {
      const next = { ...prev };
      const cur = next[key];
      if (cur && typeof cur === "object" && !Array.isArray(cur)) {
        // shouldn't hit here — handled by nested editor below
        return prev;
      }
      next[key] = coerce(cur, raw);
      return next;
    });
  };

  const updateNested = (parent: string, child: string, raw: string) => {
    commit((prev) => {
      const next = { ...prev };
      const obj = { ...(next[parent] as Json) };
      obj[child] = coerce(obj[child], raw);
      next[parent] = obj;
      return next;
    });
  };

  const updateRowCell = (
    tableKey: string,
    rowIdx: number,
    col: string,
    raw: string
  ) => {
    commit((prev) => {
      const next = { ...prev };
      const arr = [...((next[tableKey] as Json[]) ?? [])];
      const row = { ...arr[rowIdx] };
      row[col] = coerce(row[col], raw);
      arr[rowIdx] = row;
      next[tableKey] = arr;
      return next;
    });
  };

  const addRow = (tableKey: string) => {
    commit((prev) => {
      const next = { ...prev };
      const arr = [...((next[tableKey] as Json[]) ?? [])];
      const template: Json = arr[0] ? Object.fromEntries(Object.keys(arr[0]).map((k) => [k, ""])) : {};
      arr.push(template);
      next[tableKey] = arr;
      return next;
    });
  };

  const deleteRow = (tableKey: string, rowIdx: number) => {
    commit((prev) => {
      const next = { ...prev };
      const arr = [...((next[tableKey] as Json[]) ?? [])];
      arr.splice(rowIdx, 1);
      next[tableKey] = arr;
      return next;
    });
  };

  const reset = () => {
    setHistory((h) => [...h, deepClone(draft)]);
    setDraft(deepClone(data));
  };

    
    const approve = () => {
        const hasEdits =
            JSON.stringify(draft) !== JSON.stringify(originalRef.current);

        const dataToSend = hasEdits
            ? draft
            : originalRef.current;

        console.log("hasEdits", hasEdits);

        onApprove(dataToSend, hasEdits);
        onClose();
    };


  // ─── Dynamic sizing based on field counts ───────────────────────────
  // Count widest line-item column set so the modal scales to fit.
  const widestRowCols = useMemo(() => {
    let max = 0;
    for (const tk of tableKeys) {
      const rows = (draft[tk] as Json[]) ?? [];
      if (rows[0]) {
        const c = Object.keys(rows[0]).filter(
          (k) => typeof rows[0][k] !== "object" || rows[0][k] === null
        ).length;
        if (c > max) max = c;
      }
    }
    return max;
  }, [draft, tableKeys]);

  const totalScalarFields = scalarKeys.reduce((acc, k) => {
    const v = draft[k];
    if (v && typeof v === "object" && !Array.isArray(v)) {
      return acc + Object.keys(v as Json).filter((nk) => {
        const nv = (v as Json)[nk];
        return typeof nv !== "object" || nv === null;
      }).length;
    }
    return acc + 1;
  }, 0);

  // Pick modal max-width tier based on biggest content
  const fieldFootprint = Math.max(totalScalarFields, widestRowCols * 2);
  const modalMaxWidth =
    fieldFootprint <= 6
      ? "max-w-2xl"
      : fieldFootprint <= 14
      ? "max-w-4xl"
      : fieldFootprint <= 24
      ? "max-w-5xl"
      : "max-w-6xl";

  // Pick grid columns for scalar section
  const scalarGridCols =
    totalScalarFields <= 4
      ? "grid-cols-1 sm:grid-cols-2"
      : totalScalarFields <= 9
      ? "grid-cols-1 sm:grid-cols-2 lg:grid-cols-3"
      : "grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4";

  // Pick grid columns for line item rows
  const rowGridCols = (colCount: number) =>
    colCount <= 4
      ? "grid-cols-1 sm:grid-cols-2"
      : colCount <= 8
      ? "grid-cols-1 sm:grid-cols-2 lg:grid-cols-3"
      : "grid-cols-1 sm:grid-cols-2 lg:grid-cols-3 xl:grid-cols-4";

  if (!open) return null;

  return (
    <AnimatePresence>
      <motion.div
        initial={{ opacity: 0 }}
        animate={{ opacity: 1 }}
        exit={{ opacity: 0 }}
        className="fixed inset-0 z-50 bg-background/80 backdrop-blur-sm flex items-center justify-center p-4"
        onClick={onClose}
      >
        <motion.div
          initial={{ opacity: 0, scale: 0.96, y: 12 }}
          animate={{ opacity: 1, scale: 1, y: 0 }}
          exit={{ opacity: 0, scale: 0.96, y: 12 }}
          transition={{ duration: 0.2 }}
          onClick={(e) => e.stopPropagation()}
          className={`glass-panel w-full ${modalMaxWidth} max-h-[92vh] flex flex-col overflow-hidden border border-border/50 rounded-xl shadow-2xl transition-all duration-200`}
        >
          {/* Header */}
          <div className="flex items-center justify-between px-5 py-3 border-b border-border/40">
            <div className="flex items-center gap-2">
              <FileEdit className="h-4 w-4 text-primary" />
              <h2 className="text-sm font-semibold text-foreground">
                Edit Invoice Fields
              </h2>
              <span className="text-[11px] text-muted-foreground/70 ml-1">
                Changes apply to JSON, exports & AI context after approval
              </span>
            </div>
            <button
              type="button"
              onClick={onClose}
              className="p-1.5 rounded-md text-muted-foreground hover:text-foreground hover:bg-secondary/50 transition-colors"
              aria-label="Close"
            >
              <X className="h-4 w-4" />
            </button>
          </div>

          {/* Body */}
          <div className="flex-1 overflow-auto custom-scrollbar p-5 space-y-6">
            {/* Scalars + nested objects */}
            {scalarKeys.length > 0 && (
              <section>
                <h3 className="text-[11px] uppercase tracking-wider text-muted-foreground mb-3">
                  Fields
                </h3>
                <div className={`grid ${scalarGridCols} gap-x-4 gap-y-3`}>
                  {scalarKeys.map((key) => {
                    const val = draft[key];
                    // nested object → render its scalar children
                    if (val && typeof val === "object" && !Array.isArray(val)) {
                      return (
                        <div key={key} className="sm:col-span-2 mt-2">
                          <div className="text-[11px] uppercase tracking-wider text-muted-foreground/80 mb-2">
                            {humanize(key)}
                          </div>
                          <div className={`grid ${scalarGridCols} gap-x-4 gap-y-3 pl-3 border-l border-border/40`}>
                            {Object.entries(val as Json)
                              .filter(([, nv]) => typeof nv !== "object" || nv === null)
                              .map(([nk, nv]) => (
                                <DynamicField
                                  key={`${key}.${nk}`}
                                  fieldKey={nk}
                                  value={nv}
                                  onChange={(raw) => updateNested(key, nk, raw)}
                                />
                              ))}
                          </div>
                        </div>
                      );
                    }
                    return (
                      <DynamicField
                        key={key}
                        fieldKey={key}
                        value={val}
                        onChange={(raw) => updateScalar(key, raw)}
                      />
                    );
                  })}
                </div>
              </section>
            )}

            {/* Tables — line items */}
            {tableKeys.map((tk) => {
              const rows = (draft[tk] as Json[]) ?? [];
              const cols =
                rows[0] != null
                  ? Object.keys(rows[0]).filter(
                      (c) => typeof rows[0][c] !== "object" || rows[0][c] === null
                    )
                  : [];
              return (
                <section
                  key={tk}
                  className="rounded-xl border-2 border-amber-500/40 bg-amber-500/5 p-4 shadow-[inset_0_0_0_1px_hsl(var(--background))]"
                >
                  {/* Section banner — calls attention that this is line-item editing */}
                  <div className="flex items-center justify-between mb-3">
                    <div className="flex items-center gap-2">
                      <span className="flex items-center justify-center h-7 w-7 rounded-md bg-amber-500/20 text-amber-500 ring-1 ring-amber-500/40">
                        <ListOrdered className="h-4 w-4" />
                      </span>
                      <div className="flex flex-col">
                        <h3 className="text-sm font-bold uppercase tracking-wider text-amber-500 flex items-center gap-2">
                          {humanize(tk)}
                          <span className="text-[10px] font-semibold text-amber-500/80 normal-case tracking-normal px-1.5 py-0.5 rounded bg-amber-500/15 border border-amber-500/30">
                            {rows.length} row{rows.length !== 1 ? "s" : ""}
                          </span>
                        </h3>
                        <span className="text-[10px] text-amber-500/70 flex items-center gap-1 mt-0.5">
                          <AlertTriangle className="h-3 w-3" />
                          You are editing line item fields — changes will affect totals & exports
                        </span>
                      </div>
                    </div>
                    <Button
                      type="button"
                      variant="outline"
                      size="sm"
                      onClick={() => addRow(tk)}
                      className="h-7 text-xs border-amber-500/40 hover:bg-amber-500/10 hover:border-amber-500/60"
                    >
                      <Plus className="h-3 w-3" /> Add row
                    </Button>
                  </div>
                  {cols.length === 0 ? (
                    <p className="text-xs text-muted-foreground/70 italic">
                      No rows yet. Click "Add row" to insert one.
                    </p>
                  ) : (
                    <div className="space-y-3">
                      {rows.map((row, i) => (
                        <div
                          key={i}
                          className="rounded-lg border-2 border-amber-500/30 bg-background/60 overflow-hidden ring-1 ring-amber-500/10"
                        >
                          {/* Row header */}
                          <div className="flex items-center justify-between px-3 py-2 bg-amber-500/10 border-b-2 border-amber-500/30">
                            <div className="flex items-center gap-2">
                              <span className="flex items-center justify-center h-6 w-6 rounded-md bg-amber-500/20 text-amber-500 text-xs font-bold ring-1 ring-amber-500/40">
                                {i + 1}
                              </span>
                              <span className="text-xs font-semibold text-amber-500/90 uppercase tracking-wide">
                                {humanize(tk).replace(/s$/, "")} #{i + 1}
                              </span>
                            </div>
                            <button
                              type="button"
                              onClick={() => deleteRow(tk, i)}
                              className="p-1.5 rounded-md text-muted-foreground hover:text-destructive hover:bg-destructive/10 transition-colors"
                              aria-label="Delete row"
                            >
                              <Trash2 className="h-3.5 w-3.5" />
                            </button>
                          </div>
                          {/* Row fields — dynamically sized to content */}
                          <div className={`grid ${rowGridCols(cols.length)} gap-x-4 gap-y-3 p-3`}>
                            {cols.map((c) => (
                              <DynamicField
                                key={c}
                                fieldKey={c}
                                value={row[c]}
                                onChange={(raw) => updateRowCell(tk, i, c, raw)}
                              />
                            ))}
                          </div>
                        </div>
                      ))}
                    </div>
                  )}
                </section>
              );
            })}
          </div>

          {/* Footer */}
          <div className="flex items-center justify-between px-5 py-3 border-t border-border/40 bg-secondary/20">
            <div className="flex items-center gap-2">
              <Button
                type="button"
                variant="ghost"
                size="sm"
                onClick={reset}
                className="text-xs"
              >
                <RotateCcw className="h-3.5 w-3.5" /> Reset
              </Button>
            </div>
            <div className="flex items-center gap-2">
              <Button
                type="button"
                variant="outline"
                size="sm"
                onClick={onClose}
                className="text-xs"
              >
                Cancel
              </Button>
                          <Button
                              type="button"
                              size="sm"
                              onClick={approve}
                              className="text-xs bg-primary hover:bg-primary/90"
                                 
                          >
                              <Check className="h-3.5 w-3.5" /> Approve changes
                          </Button>
            </div>
          </div>
        </motion.div>
      </motion.div>
    </AnimatePresence>
  );
}
