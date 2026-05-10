import { useState, useEffect, useRef, useMemo } from "react";
import type { MappedWorkspaceData } from "@/services/dataMapper";

export interface ProcessingState {
  phase: "uploading" | "analyzing" | "extracting" | "validating" | "complete";
  progress: number;
  revealedFieldIds: Set<string>;
  revealedLineItemIds: Set<string>;
  pdfBlur: number;
  isComplete: boolean;
  aiNarrations: string[];
  phaseLabel: string;
}

const PHASE_LABELS: Record<string, string> = {
  uploading: "Uploading document…",
  analyzing: "Analyzing document structure…",
  extracting: "Extracting key fields…",
  validating: "Validating and cross-referencing…",
  complete: "Processing complete",
};

const WAITING_NARRATIONS: { at: number; text: string }[] = [
  { at: 2, text: "📄 Document received. Starting analysis…" },
  { at: 8, text: "🔍 Analyzing document structure…" },
  { at: 16, text: "📝 Sending to extraction engine…" },
  { at: 28, text: "⚙️ Model is processing the document…" },
  { at: 42, text: "🔢 Still extracting — complex document layout detected…" },
  { at: 55, text: "⏳ Almost there — finalizing extraction…" },
];

// ── Generic reveal helpers ──

function getAllFields(data: MappedWorkspaceData) {
  return data.sections.flatMap(s => s.fields);
}

function getAllTableRowIds(data: MappedWorkspaceData) {
  const ids: { id: string }[] = [];
  for (const table of data.tables) {
    table.rows.forEach((_, i) => ids.push({ id: `${table.key}-row-${i}` }));
  }
  return ids;
}

function buildRevealNarrations(data: MappedWorkspaceData): { at: number; text: string }[] {
  const narr: { at: number; text: string }[] = [];
  const fields = getAllFields(data);
  const rows = getAllTableRowIds(data);

  narr.push({ at: 62, text: `✅ Extraction complete! Detected **${data.documentMeta.vendor}**` });

  if (fields.length > 0)
    narr.push({ at: 67, text: `✅ **${fields[0].label}**: ${fields[0].value} (${fields[0].confidence}% confidence)` });
  if (fields.length > 1)
    narr.push({ at: 72, text: `✅ **${fields[1].label}**: ${fields[1].value} (${fields[1].confidence}%)` });
  if (fields.length > 2)
    narr.push({ at: 76, text: `✅ Extracted **${fields.slice(2, 5).map(x => x.label).join("**, **")}** and more…` });

  if (data.tables.length > 0) {
    narr.push({ at: 80, text: `📊 Revealing ${data.tables.map(t => `${t.rows.length} ${t.label}`).join(", ")}…` });
  } else {
    narr.push({ at: 80, text: "🔍 Checking all extracted values…" });
  }

  if (rows.length > 0) narr.push({ at: 85, text: `📊 ${rows.length} rows found across ${data.tables.length} table(s)` });

  const warns = data.validationIssues.filter(v => v.type === "warning");
  if (warns.length > 0) narr.push({ at: 90, text: `⚠️ **${warns[0].detail}**` });

  const errs = data.validationIssues.filter(v => v.type === "error");
  narr.push({
    at: 95,
    text: `✅ Done. **${fields.length} fields**, **${rows.length} rows**.${errs.length > 0 ? ` ${errs.length} error(s).` : ""}`,
  });

  return narr;
}

function buildFieldSchedule(data: MappedWorkspaceData): { at: number; id: string }[] {
  const all = getAllFields(data);
  const start = 65, end = 88;
  const step = all.length > 1 ? (end - start) / (all.length - 1) : 0;
  return all.map((f, i) => ({ at: Math.round(start + i * step), id: f.id }));
}

function buildRowSchedule(data: MappedWorkspaceData): { at: number; id: string }[] {
  const all = getAllTableRowIds(data);
  const start = 83, end = 93;
  const step = all.length > 1 ? (end - start) / (all.length - 1) : 0;
  return all.map((r, i) => ({ at: Math.round(start + i * step), id: r.id }));
}

export function useProcessingState(
  startProcessing = true,
  workspaceData?: MappedWorkspaceData | null
) {
  const [progress, setProgress] = useState(startProcessing ? 0 : 100);
  const [revealedFieldIds, setRevealedFieldIds] = useState<Set<string>>(new Set());
  const [revealedLineItemIds, setRevealedLineItemIds] = useState<Set<string>>(new Set());
  const [aiNarrations, setAiNarrations] = useState<string[]>([]);
  const emittedNarrations = useRef<Set<number>>(new Set());
  const emittedFields = useRef<Set<string>>(new Set());
  const emittedRows = useRef<Set<string>>(new Set());
  const dataArrivedAt = useRef<number | null>(null);

  const hasData = !!workspaceData;
  const isComplete = progress >= 100;

  const revealSchedules = useMemo(() => {
    if (!workspaceData) return null;
    return {
      fields: buildFieldSchedule(workspaceData),
      rows: buildRowSchedule(workspaceData),
      narrations: buildRevealNarrations(workspaceData),
    };
  }, [workspaceData]);

  // Phase A: slow crawl 0→60%
  useEffect(() => {
    if (!startProcessing || hasData || progress >= 60) return;
    const interval = setInterval(() => {
      setProgress(prev => {
        if (prev >= 58) return 58;
        const speed = prev < 15 ? 1.8 : prev < 35 ? 0.8 : prev < 50 ? 0.5 : 0.3;
        return Math.min(58, prev + speed);
      });
    }, 500);
    return () => clearInterval(interval);
  }, [startProcessing, hasData, progress]);

  // Phase A narrations
  useEffect(() => {
    if (hasData) return;
    WAITING_NARRATIONS.forEach(({ at, text }) => {
      if (progress >= at && !emittedNarrations.current.has(at)) {
        emittedNarrations.current.add(at);
        setAiNarrations(prev => [...prev, text]);
      }
    });
  }, [progress, hasData]);

  // Data arrived → jump to Phase B
  useEffect(() => {
    if (!workspaceData || dataArrivedAt.current) return;
    dataArrivedAt.current = Date.now();
    setProgress(prev => Math.max(prev, 60));
  }, [workspaceData]);

  // Phase B: fast reveal 60→100%
  useEffect(() => {
    if (!hasData || !dataArrivedAt.current || isComplete || progress < 60) return;
    const interval = setInterval(() => {
      setProgress(prev => prev >= 100 ? 100 : Math.min(100, prev + 1.5));
    }, 200);
    return () => clearInterval(interval);
  }, [hasData, isComplete, progress]);

  // Phase B: reveal fields and rows from real data
  useEffect(() => {
    if (!revealSchedules || progress < 60) return;

    revealSchedules.fields.forEach(({ at, id }) => {
      if (progress >= at && !emittedFields.current.has(id)) {
        emittedFields.current.add(id);
        setRevealedFieldIds(prev => new Set(prev).add(id));
      }
    });

    revealSchedules.rows.forEach(({ at, id }) => {
      if (progress >= at && !emittedRows.current.has(id)) {
        emittedRows.current.add(id);
        setRevealedLineItemIds(prev => new Set(prev).add(id));
      }
    });

    revealSchedules.narrations.forEach(({ at, text }) => {
      if (progress >= at && !emittedNarrations.current.has(at)) {
        emittedNarrations.current.add(at);
        setAiNarrations(prev => [...prev, text]);
      }
    });
  }, [progress, revealSchedules]);

  const phase: ProcessingState["phase"] =
    progress < 8 ? "uploading" : progress < 25 ? "analyzing" : progress < 60 ? "extracting" : progress < 95 ? "validating" : "complete";

  return {
    phase,
    phaseLabel: PHASE_LABELS[phase],
    progress: Math.round(progress),
    revealedFieldIds,
    revealedLineItemIds,
    pdfBlur: 0,
    isComplete,
    aiNarrations,
  };
}
