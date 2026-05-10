import { useMemo, useEffect, useRef, useState, useCallback } from "react";
import { ChevronRight, FileText } from "lucide-react";
import { useNavigate } from "react-router-dom";
import { Panel, PanelGroup, PanelResizeHandle } from "react-resizable-panels";
import { TopActionBar } from "@/components/workspace/TopActionBar";
import { PdfViewerPanel } from "@/components/workspace/PdfViewerPanel";
import { StructuredDataPanel } from "@/components/workspace/StructuredDataPanel";
import { AIAssistantPanel } from "@/components/workspace/AIAssistantPanel";
import { useProcessingState } from "@/hooks/useProcessingState";
import { useExtractionResult } from "@/hooks/useExtractionResult";
import { mapExtractionToWorkspace } from "@/services/dataMapper";
import {
  extractDocument,
  extractDocumentDemo,
  isDemoMode,
  approveExtraction,
  type ExtractionResponse,
} from "@/services/api";

const DocumentWorkspace = () => {
  const { file, documentType, result, fileName, setResult, isSample, stageFile } =
    useExtractionResult();
  const extractionTriggered = useRef(false);
  const [extractionError, setExtractionError] = useState<string | null>(null);
  // Tracks whether the user has approved local edits to the extracted data.
  // When true, we send only the edited `data` to the chat backend (omit
  // `request_id`) so the model answers from the user's latest values rather
  // than the server's original extraction.
  const [hasLocalEdits, setHasLocalEdits] = useState(false);


    const handleApproveInvoiceEdits = useCallback(
        (updatedData: Record<string, unknown>, hasEdits: boolean) => {
            if (!result) return;

            // UPDATE FRONTEND STATE
            setResult(
                {
                    ...result,
                    data: updatedData, // edited OR original data
                },
                fileName
            );

            // TRACK LOCAL EDIT STATE
            setHasLocalEdits(hasEdits);

            //SEND TO BACKEND
            const payload = {
                requestId: result.request_id,
                hasEdits,
                data: updatedData,
            };

            console.log("FINAL APPROVED PAYLOAD", payload);
            approveExtraction(payload);
        },
        [result, setResult, fileName]
    );
   

  // ── Layout state ──
    const [pdfExpanded, setPdfExpanded] = useState(false);
    const [editorOpen, setEditorOpen] = useState(false);
  /** VS-style auto-hide for the left document preview — frees width for extracted data */
  const [pdfPanelCollapsed, setPdfPanelCollapsed] = useState(false);
  const [aiVisible, setAiVisible] = useState(true);
  const togglePdfExpand = useCallback(() => setPdfExpanded((v) => !v), []);
  const toggleAi = useCallback(() => setAiVisible((v) => !v), []);
  const collapsePdfPanel = useCallback(() => setPdfPanelCollapsed(true), []);
  const expandPdfPanel = useCallback(() => setPdfPanelCollapsed(false), []);

  // Re-upload: stage the new file with the current doc type, then route through
  // the same /upload activation animation pipeline used from the homescreen.
  const navigate = useNavigate();
  const handleUploadNew = useCallback(
    (newFile: File) => {
      console.log("[DocIQ] Re-uploading via /upload pipeline:", newFile.name, "type:", documentType);
      extractionTriggered.current = false;
      setExtractionError(null);
      setHasLocalEdits(false);
      stageFile(newFile, documentType, false);
      navigate("/upload");
    },
    [documentType, stageFile, navigate]
  );

  useEffect(() => {
    if (pdfExpanded) setPdfPanelCollapsed(false);
  }, [pdfExpanded]);

  // ── Keyboard shortcuts ──
  useEffect(() => {
    const handler = (e: KeyboardEvent) => {
      if ((e.metaKey || e.ctrlKey) && e.key.toLowerCase() === "m") { e.preventDefault(); togglePdfExpand(); }
      if ((e.metaKey || e.ctrlKey) && e.key === "\\") { e.preventDefault(); toggleAi(); }
      if (e.key === "Escape" && pdfExpanded) { e.preventDefault(); setPdfExpanded(false); }
      if ((e.ctrlKey || e.metaKey) && e.altKey && e.key.toLowerCase() === "d") {
        e.preventDefault();
        if (!pdfExpanded) setPdfPanelCollapsed((c) => !c);
      }
    };
    window.addEventListener("keydown", handler);
    return () => window.removeEventListener("keydown", handler);
  }, [pdfExpanded, togglePdfExpand, toggleAi]);

  // ── Extraction: generic for any document type ──
  useEffect(() => {
    if (result || extractionTriggered.current) return;
    extractionTriggered.current = true;

    const targetFile = file ?? (() => {
      const blob = new Blob(["demo"], { type: "application/pdf" });
      return new File([blob], "Demo_Document.pdf", { type: "application/pdf" });
    })();

    const useDemoMode = isDemoMode() || !file || isSample;

    if (useDemoMode) {
      console.log("[DocIQ] Demo mode — using simulated extraction for type:", documentType);
      extractDocumentDemo(targetFile, undefined, documentType).then((r) => setResult(r, targetFile.name));
      return;
    }

    console.log("[DocIQ] Calling real API for:", file!.name, "type:", documentType);
    extractDocument(file!, { documentType })
      .then((r) => {
        console.log("[DocIQ] Extraction succeeded:", r.status, "| fields:", Object.keys(r.data ?? {}).length);
        setResult(r, file!.name);
      })
      .catch((err) => {
        const msg = err instanceof Error ? err.message : String(err);
        console.error("[DocIQ] Extraction failed:", msg);
        setExtractionError(msg);
        extractDocumentDemo(file!, undefined, documentType).then((r) => setResult(r, file!.name));
      });
  }, [file, result, documentType, setResult, isSample]);

  // Map result → generic workspace data
  const workspaceData = useMemo(() => {
    if (!result) return null;
    return mapExtractionToWorkspace(result, fileName);
  }, [result, fileName]);

  const processing = useProcessingState(true, workspaceData);

  const chatContext = useMemo(() => {
    if (!result?.data) return null;
    return {
      // When the user has approved local edits, omit the server-side
      // request_id so the chat backend uses our edited `data` payload
      // instead of the originally-stored extraction.
      request_id: hasLocalEdits ? "" : result.request_id,
      data: result.data as Record<string, unknown>,
      document_type: result.document_type,
      source_file: result.metadata.source_file,
    };
  }, [result, hasLocalEdits]);

  // ── Layout sizing — drag-resizable panels with sensible defaults ──
  const isWellPlan = workspaceData?.documentType === "well_plan";
  const pdfRail = pdfPanelCollapsed && !pdfExpanded;

  // Default percentages for the 3 panels
  const defaultPdf = isWellPlan ? (aiVisible ? 32 : 38) : aiVisible ? 50 : 55;
  const defaultAi = aiVisible ? (isWellPlan ? 25 : 20) : 0;
  const defaultData = 100 - defaultPdf - defaultAi;


    const handleBeforeEditOpen = useCallback(() => {
        setAiVisible(false);
        setEditorOpen(true); 
    }, []);

    const handleEditorClose = useCallback(() => {
        setEditorOpen(false);
    }, []);


  // A nicely-styled, draggable handle (the "red marked" gutter the user pointed to)
  const ResizeHandle = ({ id }: { id: string }) => (
    <PanelResizeHandle
      id={id}
      className="group relative w-2 shrink-0 flex items-center justify-center outline-none"
      aria-label="Drag to resize"
    >
      <div className="h-full w-[3px] rounded-full bg-border/40 group-hover:bg-primary/60 group-data-[resize-handle-state=drag]:bg-primary transition-colors" />
      <div className="absolute inset-y-0 left-1/2 -translate-x-1/2 w-1 opacity-0 group-hover:opacity-100 group-data-[resize-handle-state=drag]:opacity-100 transition-opacity">
        <div className="h-full w-full" />
      </div>
      {/* Subtle grip dots so users see it's draggable */}
      <div className="absolute top-1/2 left-1/2 -translate-x-1/2 -translate-y-1/2 flex flex-col gap-1 opacity-50 group-hover:opacity-100 transition-opacity pointer-events-none">
        <span className="h-1 w-1 rounded-full bg-muted-foreground/70" />
        <span className="h-1 w-1 rounded-full bg-muted-foreground/70" />
        <span className="h-1 w-1 rounded-full bg-muted-foreground/70" />
      </div>
    </PanelResizeHandle>
  );

  return (
    <div className="h-screen flex flex-col bg-background overflow-hidden">
      <div className="px-6 pt-4 pb-0">
              <TopActionBar
                  processing={processing}
                  documentMeta={
                      workspaceData?.documentMeta ?? {
                          fileName: fileName || "Processing…",
                          vendor: "Detecting…",
                          status: "Extracting",
                          confidenceLevel: "Analyzing",
                          processingTime: "—",
                      }
                  }
                  extractionResult={result}
                  pdfExpanded={pdfExpanded}
                  onTogglePdfExpand={togglePdfExpand}
                  aiVisible={aiVisible}
                  onToggleAi={toggleAi}
                  onUploadNew={handleUploadNew}

                

              />
        {extractionError && (
          <div className="mt-2 px-4 py-2 rounded-lg bg-warning/10 border border-warning/20 text-xs text-warning flex items-center gap-2">
            <span className="w-1.5 h-1.5 rounded-full bg-warning flex-shrink-0" />
            Backend error: {extractionError} — showing demo data
            <button onClick={() => setExtractionError(null)} className="ml-auto text-warning/60 hover:text-warning">✕</button>
          </div>
        )}
      </div>

      <div className="flex-1 px-6 py-4 flex gap-2 min-h-0 w-full min-w-0">
        {/* Optional collapsed PDF rail */}
        {pdfRail && (
          <div
            className="flex flex-col items-center shrink-0 w-10 rounded-lg border border-border/40 bg-card/50 backdrop-blur-sm py-3 gap-2 shadow-sm"
            title="Document preview hidden — click to show"
          >
            <button
              type="button"
              onClick={expandPdfPanel}
              className="p-1.5 rounded-md border border-border/30 hover:bg-secondary text-muted-foreground hover:text-foreground transition-colors"
              aria-label="Show document preview"
              title="Show document preview (Ctrl+Alt+D)"
            >
              <ChevronRight className="h-4 w-4" />
            </button>
            <FileText className="h-3.5 w-3.5 text-muted-foreground/45 shrink-0" aria-hidden />
            <span
              className="text-[8px] font-semibold text-muted-foreground/40 tracking-[0.2em] uppercase select-none leading-tight text-center max-w-[2.5rem]"
              style={{ writingMode: "vertical-rl", transform: "rotate(180deg)" }}
            >
              Doc
            </span>
          </div>
        )}

        <PanelGroup
          direction="horizontal"
          autoSaveId={`workspace-layout-${isWellPlan ? "wp" : "doc"}-${aiVisible ? "ai" : "noai"}-${pdfRail ? "rail" : "full"}-${pdfExpanded ? "exp" : "norm"}`}
          className="flex-1 min-w-0"
        >
          {/* PDF panel */}
          {!pdfRail && (
            <>
              <Panel
                id="pdf"
                order={1}
                defaultSize={pdfExpanded ? 100 : defaultPdf}
                minSize={pdfExpanded ? 100 : 20}
                maxSize={pdfExpanded ? 100 : 80}
              >
                <PdfViewerPanel
                  file={file}
                  isComplete={processing.isComplete}
                  progress={processing.progress}
                  isExpanded={pdfExpanded}
                  onToggleExpand={togglePdfExpand}
                  onCollapseSidePanel={collapsePdfPanel}
                />
              </Panel>
              {!pdfExpanded && <ResizeHandle id="rh-pdf-data" />}
            </>
          )}

          {/* Extracted Data panel */}
          {!pdfExpanded && (
            <Panel id="data" order={2} defaultSize={defaultData} minSize={20}>

                          <StructuredDataPanel
                              revealedFieldIds={processing.revealedFieldIds}
                              revealedLineItemIds={processing.revealedLineItemIds}
                              isComplete={processing.isComplete}
                              workspaceData={workspaceData}
                              extractionResult={result}
                              onApproveInvoiceEdits={handleApproveInvoiceEdits}
                              onBeforeEditOpen={handleBeforeEditOpen}
                              onEditorClose={handleEditorClose}   
                          />

            </Panel>
          )}

          {/* AI Assistant panel */}
          {aiVisible && !pdfExpanded && (
            <>
              <ResizeHandle id="rh-data-ai" />
              <Panel id="ai" order={3} defaultSize={defaultAi || 20} minSize={15} maxSize={45}>
                <AIAssistantPanel
                  narrations={processing.aiNarrations}
                  isProcessing={!processing.isComplete}
                  summaryMessage={workspaceData?.aiSummaryMessage}
                  chatContext={chatContext}
                />
              </Panel>
            </>
          )}
        </PanelGroup>
      </div>
    </div>
  );
};

export default DocumentWorkspace;
