import { useState, useEffect, useCallback } from "react";
import { useNavigate, useSearchParams } from "react-router-dom";
import { motion, AnimatePresence } from "framer-motion";
import {
  ChevronLeft, ChevronRight, Plus, Settings, FileText, Code,
  Shield, TestTube, Save, Trash2, Eye, EyeOff, Zap, Sparkles,
  Receipt, Clock, Clipboard, Target, ShoppingCart, Truck,
  HardHat, Wrench, FileSpreadsheet, AlertTriangle, Check,
  Upload as UploadIcon, Play, Loader2, X, RefreshCw,
  Layers, Brain, Cog,
} from "lucide-react";
import {
  fetchDocumentTypes, fetchDocumentTypePrompts, createDocumentType,
  updateDocumentType, deleteDocumentType, testExtract, isDemoMode,
  getDemoDocumentTypes,
  type DocumentTypeSummary, type CreateDocumentTypeRequest,
  type UpdateDocumentTypeRequest, type ExtractionResponse, type ValidationRule,
} from "@/services/api";

// ═══════════════════════════════════════════════════════════════════
//  Icon Registry — maps icon_name strings to Lucide components
// ═══════════════════════════════════════════════════════════════════

const ICON_MAP: Record<string, React.ReactNode> = {
  "receipt": <Receipt className="h-5 w-5" />,
  "clock": <Clock className="h-5 w-5" />,
  "clipboard": <Clipboard className="h-5 w-5" />,
  "target": <Target className="h-5 w-5" />,
  "shopping-cart": <ShoppingCart className="h-5 w-5" />,
  "truck": <Truck className="h-5 w-5" />,
  "hard-hat": <HardHat className="h-5 w-5" />,
  "wrench": <Wrench className="h-5 w-5" />,
  "file-text": <FileText className="h-5 w-5" />,
  "file-spreadsheet": <FileSpreadsheet className="h-5 w-5" />,
  "layers": <Layers className="h-5 w-5" />,
  "brain": <Brain className="h-5 w-5" />,
  "zap": <Zap className="h-5 w-5" />,
  "settings": <Settings className="h-5 w-5" />,
};

const ICON_OPTIONS = Object.keys(ICON_MAP);

const CATEGORY_OPTIONS = [
  "Finance", "Procurement", "HR", "Operations", "Field Operations",
  "Safety", "Compliance", "Legal", "Engineering", "General",
];

const REASONING_OPTIONS = ["low", "medium", "high"];

// ═══════════════════════════════════════════════════════════════════
//  Wizard Steps
// ═══════════════════════════════════════════════════════════════════

type WizardStep = "identity" | "system_prompt" | "extraction_prompt" | "schema" | "settings" | "test";

const STEPS: { id: WizardStep; label: string; icon: React.ReactNode; desc: string }[] = [
  { id: "identity", label: "Identity", icon: <Sparkles className="h-4 w-4" />, desc: "Name, category, file types" },
  { id: "system_prompt", label: "System Prompt", icon: <Brain className="h-4 w-4" />, desc: "AI role & extraction rules" },
  { id: "extraction_prompt", label: "User Prompt", icon: <FileText className="h-4 w-4" />, desc: "Field-by-field instructions" },
  { id: "schema", label: "Schema", icon: <Code className="h-4 w-4" />, desc: "JSON output structure" },
  { id: "settings", label: "Settings", icon: <Cog className="h-4 w-4" />, desc: "Model, dual-pass, validation" },
  { id: "test", label: "Test", icon: <TestTube className="h-4 w-4" />, desc: "Upload sample & verify" },
];

// ═══════════════════════════════════════════════════════════════════
//  Form State
// ═══════════════════════════════════════════════════════════════════

interface FormState {
  typeId: string;
  displayName: string;
  description: string;
  version: string;
  enabled: boolean;
  category: string;
  iconName: string;
  acceptedExtensions: string[];
  maxFileSizeMb: number;
  maxPages: number;
  systemPrompt: string;
  extractionPrompt: string;
  jsonSchema: string;
  reasoningEffort: string;
  maxTokens: number;
  maxPagesForVision: number;
  dualPassEnabled: boolean;
  dualPassCriticalFields: string[];
  excelExportEnabled: boolean;
  validationRules: ValidationRule[];
}

const EMPTY_FORM: FormState = {
  typeId: "", displayName: "", description: "", version: "1.0.0",
  enabled: true, category: "General", iconName: "file-text",
  acceptedExtensions: [".pdf"], maxFileSizeMb: 50, maxPages: 30,
  systemPrompt: "", extractionPrompt: "", jsonSchema: "",
  reasoningEffort: "medium", maxTokens: 8192, maxPagesForVision: 12,
  dualPassEnabled: true, dualPassCriticalFields: [],
  excelExportEnabled: true, validationRules: [],
};

// ═══════════════════════════════════════════════════════════════════
//  Main Component
// ═══════════════════════════════════════════════════════════════════

export default function DocTypeStudio() {
  const navigate = useNavigate();
  const [searchParams] = useSearchParams();
  const editId = searchParams.get("edit");

  const [docTypes, setDocTypes] = useState<DocumentTypeSummary[]>([]);
  const [loading, setLoading] = useState(true);
  const [mode, setMode] = useState<"list" | "create" | "edit">(editId ? "edit" : "list");
  const [currentStep, setCurrentStep] = useState<WizardStep>("identity");
  const [form, setForm] = useState<FormState>(EMPTY_FORM);
  const [saving, setSaving] = useState(false);
  const [testResult, setTestResult] = useState<ExtractionResponse | null>(null);
  const [testFile, setTestFile] = useState<File | null>(null);
  const [testing, setTesting] = useState(false);
  const [deleteConfirm, setDeleteConfirm] = useState<string | null>(null);

  // ── Load document types ──
  const loadTypes = useCallback(async () => {
    setLoading(true);
    try {
      const types = isDemoMode() ? getDemoDocumentTypes() : await fetchDocumentTypes();
      setDocTypes(types);
    } catch (err) {
      console.error("Failed to load document types:", err);
      setDocTypes(getDemoDocumentTypes());
    }
    setLoading(false);
  }, []);

  useEffect(() => { loadTypes(); }, [loadTypes]);

  // ── Load existing type for editing ──
  useEffect(() => {
    if (editId && docTypes.length > 0) {
      const existing = docTypes.find(t => t.key === editId);
      if (existing) {
        setForm(prev => ({
          ...prev,
          typeId: existing.key,
          displayName: existing.display_name,
          description: existing.description,
          version: existing.version,
          enabled: existing.enabled,
          category: existing.category || "General",
          iconName: existing.icon_name || "file-text",
          acceptedExtensions: existing.accepted_file_types,
          maxFileSizeMb: existing.max_file_size_mb,
          maxPages: existing.max_page_count,
        }));
        setMode("edit");
        // Load prompts
        if (!isDemoMode()) {
          fetchDocumentTypePrompts(editId).then(prompts => {
            setForm(prev => ({
              ...prev,
              systemPrompt: prompts.system_prompt,
              extractionPrompt: prompts.extraction_prompt,
              jsonSchema: prompts.json_schema,
              validationRules: prompts.validation_rules,
            }));
          }).catch(console.error);
        }
      }
    }
  }, [editId, docTypes]);

  // ── Form helpers ──
  const updateForm = (updates: Partial<FormState>) => setForm(prev => ({ ...prev, ...updates }));
  const stepIndex = STEPS.findIndex(s => s.id === currentStep);

  const goNext = () => {
    if (stepIndex < STEPS.length - 1) setCurrentStep(STEPS[stepIndex + 1].id);
  };
  const goPrev = () => {
    if (stepIndex > 0) setCurrentStep(STEPS[stepIndex - 1].id);
  };

  // ── Auto-generate typeId from displayName ──
  useEffect(() => {
    if (mode === "create" && form.displayName && !form.typeId) {
      const autoId = form.displayName.toLowerCase().replace(/[^a-z0-9]+/g, "_").replace(/^_|_$/g, "");
      updateForm({ typeId: autoId });
    }
  }, [form.displayName, mode, form.typeId]);

  // ── Save ──
  const handleSave = async () => {
    setSaving(true);
    try {
      if (mode === "create") {
        const req: CreateDocumentTypeRequest = {
          type_id: form.typeId,
          display_name: form.displayName,
          description: form.description,
          version: form.version,
          enabled: form.enabled,
          category: form.category,
          icon_name: form.iconName,
          accepted_extensions: form.acceptedExtensions,
          max_file_size_mb: form.maxFileSizeMb,
          max_pages: form.maxPages,
          system_prompt: form.systemPrompt,
          extraction_prompt: form.extractionPrompt,
          json_schema: form.jsonSchema,
          reasoning_effort: form.reasoningEffort,
          max_tokens: form.maxTokens,
          max_pages_for_vision: form.maxPagesForVision,
          dual_pass_enabled: form.dualPassEnabled,
          dual_pass_critical_fields: form.dualPassCriticalFields,
          excel_export_enabled: form.excelExportEnabled,
          validation_rules: form.validationRules.length > 0 ? form.validationRules : undefined,
        };
        await createDocumentType(req);
      } else {
        const req: UpdateDocumentTypeRequest = {
          display_name: form.displayName,
          description: form.description,
          version: form.version,
          enabled: form.enabled,
          category: form.category,
          icon_name: form.iconName,
          accepted_extensions: form.acceptedExtensions,
          max_file_size_mb: form.maxFileSizeMb,
          max_pages: form.maxPages,
          system_prompt: form.systemPrompt || undefined,
          extraction_prompt: form.extractionPrompt || undefined,
          json_schema: form.jsonSchema || undefined,
          validation_rules: form.validationRules.length > 0 ? form.validationRules : undefined,
        };
        await updateDocumentType(form.typeId, req);
      }
      await loadTypes();
      setMode("list");
      setForm(EMPTY_FORM);
      setCurrentStep("identity");
    } catch (err) {
      console.error("Save failed:", err);
      alert(`Save failed: ${(err as Error).message}`);
    }
    setSaving(false);
  };

  // ── Delete ──
  const handleDelete = async (typeId: string) => {
    try {
      await deleteDocumentType(typeId);
      await loadTypes();
      setDeleteConfirm(null);
      if (mode === "edit" && form.typeId === typeId) {
        setMode("list");
        setForm(EMPTY_FORM);
      }
    } catch (err) {
      alert(`Delete failed: ${(err as Error).message}`);
    }
  };

  // ── Test Extraction ──
  const handleTestExtract = async () => {
    if (!testFile) return;
    setTesting(true);
    setTestResult(null);
    try {
      const result = await testExtract(form.typeId, testFile);
      setTestResult(result);
    } catch (err) {
      console.error("Test extraction failed:", err);
      setTestResult({
        request_id: "test-error", document_type: form.typeId, status: "Failed",
        metadata: {} as any, data: null,
        validation: { is_valid: false, confidence_score: 0, warnings: [], errors: [(err as Error).message], warning_count: 0, error_count: 1 },
        error: { code: "TEST_FAILED", message: (err as Error).message },
      });
    }
    setTesting(false);
  };

  // ═══════════════════════════════════════════════════════════════════
  //  RENDER — List View
  // ═══════════════════════════════════════════════════════════════════

  if (mode === "list") {
    return (
      <div className="min-h-screen bg-background p-4 md:p-6 lg:p-8 max-w-[1400px] mx-auto space-y-6">
        {/* Header */}
        <div className="flex items-center justify-between">
          <div className="flex items-center gap-4">
            <button onClick={() => navigate("/")}
              className="p-2 rounded-lg border border-border/50 hover:bg-secondary/50 transition-colors">
              <ChevronLeft className="h-4 w-4 text-muted-foreground" />
            </button>
            <div>
              <h1 className="text-2xl font-bold text-foreground flex items-center gap-2">
                <Settings className="h-6 w-6 text-primary" />
                Document Type Studio
              </h1>
              <p className="text-sm text-muted-foreground mt-0.5">
                Configure extraction agents — prompts, schemas, validation rules
              </p>
            </div>
          </div>
          <button onClick={() => { setMode("create"); setForm(EMPTY_FORM); setCurrentStep("identity"); }}
            className="flex items-center gap-2 px-4 py-2.5 rounded-xl bg-primary text-primary-foreground font-medium text-sm hover:bg-primary/90 transition-all btn-glow">
            <Plus className="h-4 w-4" /> New Document Type
          </button>
        </div>

        {/* Grid of existing types */}
        {loading ? (
          <div className="flex items-center justify-center py-20">
            <Loader2 className="h-6 w-6 animate-spin text-primary" />
            <span className="ml-3 text-muted-foreground">Loading document types...</span>
          </div>
        ) : (
          <div className="grid grid-cols-1 md:grid-cols-2 xl:grid-cols-3 gap-4">
            {docTypes.map((dt, i) => (
              <motion.div key={dt.key}
                initial={{ opacity: 0, y: 12 }}
                animate={{ opacity: 1, y: 0 }}
                transition={{ delay: i * 0.05 }}
                className="glass-panel-hover p-5 cursor-pointer group"
                onClick={() => {
                  navigate(`/studio?edit=${dt.key}`);
                  setMode("edit");
                }}
              >
                <div className="flex items-start justify-between mb-3">
                  <div className="flex items-center gap-3">
                    <div className="p-2.5 rounded-xl bg-primary/10 text-primary border border-primary/20">
                      {ICON_MAP[dt.icon_name || "file-text"] || <FileText className="h-5 w-5" />}
                    </div>
                    <div>
                      <h3 className="font-semibold text-foreground">{dt.display_name}</h3>
                      <span className="text-xs text-muted-foreground">{dt.category} · v{dt.version}</span>
                    </div>
                  </div>
                  <div className="flex items-center gap-2">
                    {dt.enabled ? (
                      <span className="flex items-center gap-1 text-xs text-accent">
                        <div className="status-dot-live" /> Active
                      </span>
                    ) : (
                      <span className="flex items-center gap-1 text-xs text-muted-foreground">
                        <EyeOff className="h-3 w-3" /> Disabled
                      </span>
                    )}
                  </div>
                </div>

                <p className="text-sm text-muted-foreground mb-3 line-clamp-2">{dt.description}</p>

                {/* Sample fields */}
                <div className="flex flex-wrap gap-1.5 mb-3">
                  {dt.sample_fields.slice(0, 5).map(f => (
                    <span key={f} className="px-2 py-0.5 rounded-md bg-secondary/60 text-xs text-secondary-foreground border border-border/50">
                      {f}
                    </span>
                  ))}
                  {dt.sample_fields.length > 5 && (
                    <span className="px-2 py-0.5 rounded-md text-xs text-muted-foreground">
                      +{dt.sample_fields.length - 5} more
                    </span>
                  )}
                </div>

                {/* Meta strip */}
                <div className="flex items-center gap-3 text-xs text-muted-foreground border-t border-border/30 pt-3">
                  <span>{dt.accepted_file_types.join(", ")}</span>
                  <span>·</span>
                  <span>≤{dt.max_file_size_mb}MB</span>
                  {dt.dual_pass_enabled && (
                    <>
                      <span>·</span>
                      <span className="text-primary flex items-center gap-1">
                        <Shield className="h-3 w-3" /> Dual-pass
                      </span>
                    </>
                  )}
                </div>

                {/* Delete button */}
                <div className="absolute top-3 right-3 opacity-0 group-hover:opacity-100 transition-opacity">
                  {deleteConfirm === dt.key ? (
                    <div className="flex items-center gap-1" onClick={e => e.stopPropagation()}>
                      <button onClick={() => handleDelete(dt.key)}
                        className="px-2 py-1 rounded bg-destructive/20 text-destructive text-xs hover:bg-destructive/30">
                        Confirm
                      </button>
                      <button onClick={() => setDeleteConfirm(null)}
                        className="px-2 py-1 rounded bg-secondary text-xs hover:bg-secondary/80">
                        Cancel
                      </button>
                    </div>
                  ) : (
                    <button onClick={(e) => { e.stopPropagation(); setDeleteConfirm(dt.key); }}
                      className="p-1.5 rounded-lg hover:bg-destructive/10 text-muted-foreground hover:text-destructive transition-colors">
                      <Trash2 className="h-3.5 w-3.5" />
                    </button>
                  )}
                </div>
              </motion.div>
            ))}

            {/* Add new card */}
            <motion.div
              initial={{ opacity: 0, y: 12 }}
              animate={{ opacity: 1, y: 0 }}
              transition={{ delay: docTypes.length * 0.05 }}
              onClick={() => { setMode("create"); setForm(EMPTY_FORM); setCurrentStep("identity"); }}
              className="glass-panel p-5 cursor-pointer border-dashed border-2 border-border/50 hover:border-primary/40 transition-all flex flex-col items-center justify-center min-h-[200px] gap-3 group"
            >
              <div className="p-3 rounded-xl bg-primary/5 text-primary group-hover:bg-primary/10 transition-colors">
                <Plus className="h-6 w-6" />
              </div>
              <div className="text-center">
                <p className="font-medium text-foreground">Create New Type</p>
                <p className="text-xs text-muted-foreground mt-1">Define prompts, schema & validation</p>
              </div>
            </motion.div>
          </div>
        )}
      </div>
    );
  }

  // ═══════════════════════════════════════════════════════════════════
  //  RENDER — Wizard (Create / Edit)
  // ═══════════════════════════════════════════════════════════════════

  return (
    <div className="min-h-screen bg-background p-4 md:p-6 lg:p-8 max-w-[1200px] mx-auto space-y-5">
      {/* Header */}
      <div className="flex items-center justify-between">
        <div className="flex items-center gap-4">
          <button onClick={() => { setMode("list"); setForm(EMPTY_FORM); setCurrentStep("identity"); navigate("/studio"); }}
            className="p-2 rounded-lg border border-border/50 hover:bg-secondary/50 transition-colors">
            <ChevronLeft className="h-4 w-4 text-muted-foreground" />
          </button>
          <div>
            <h1 className="text-xl font-bold text-foreground">
              {mode === "create" ? "New Document Type" : `Edit: ${form.displayName || form.typeId}`}
            </h1>
            <p className="text-sm text-muted-foreground">
              {mode === "create" ? "Configure a new extraction agent" : "Modify extraction configuration"}
            </p>
          </div>
        </div>
        <div className="flex items-center gap-2">
          <button onClick={handleSave} disabled={saving || !form.typeId || !form.displayName}
            className="flex items-center gap-2 px-5 py-2.5 rounded-xl bg-primary text-primary-foreground font-medium text-sm hover:bg-primary/90 transition-all disabled:opacity-40 disabled:cursor-not-allowed btn-glow">
            {saving ? <Loader2 className="h-4 w-4 animate-spin" /> : <Save className="h-4 w-4" />}
            {saving ? "Saving..." : "Save"}
          </button>
        </div>
      </div>

      {/* Step Navigator */}
      <div className="glass-panel p-1.5">
        <div className="flex gap-1">
          {STEPS.map((step, i) => (
            <button key={step.id}
              onClick={() => setCurrentStep(step.id)}
              className={`flex-1 flex items-center justify-center gap-2 px-3 py-2.5 rounded-lg text-sm font-medium transition-all ${
                currentStep === step.id
                  ? "bg-primary/15 text-primary border border-primary/30"
                  : i < stepIndex
                    ? "text-accent/80 hover:bg-secondary/50"
                    : "text-muted-foreground hover:bg-secondary/50"
              }`}
            >
              <span className={`flex items-center justify-center h-6 w-6 rounded-full text-xs font-bold ${
                currentStep === step.id
                  ? "bg-primary/20 text-primary"
                  : i < stepIndex
                    ? "bg-accent/20 text-accent"
                    : "bg-secondary text-muted-foreground"
              }`}>
                {i < stepIndex ? <Check className="h-3.5 w-3.5" /> : i + 1}
              </span>
              <span className="hidden lg:inline">{step.label}</span>
            </button>
          ))}
        </div>
      </div>

      {/* Step Content */}
      <AnimatePresence mode="wait">
        <motion.div key={currentStep}
          initial={{ opacity: 0, x: 20 }} animate={{ opacity: 1, x: 0 }} exit={{ opacity: 0, x: -20 }}
          transition={{ duration: 0.2 }}
          className="glass-panel p-6"
        >
          {/* ── Step 1: Identity ── */}
          {currentStep === "identity" && (
            <div className="space-y-6">
              <div className="flex items-center gap-2 mb-2">
                <Sparkles className="h-5 w-5 text-primary" />
                <h2 className="text-lg font-semibold">Document Type Identity</h2>
              </div>

              <div className="grid grid-cols-1 md:grid-cols-2 gap-5">
                <div>
                  <label className="block text-sm font-medium text-foreground mb-1.5">Display Name *</label>
                  <input value={form.displayName}
                    onChange={e => updateForm({ displayName: e.target.value, typeId: mode === "create" ? "" : form.typeId })}
                    placeholder="e.g. AP Invoice, Tour Sheet, Safety Report..."
                    className="w-full px-3 py-2.5 rounded-lg bg-secondary/50 border border-border/50 text-foreground placeholder:text-muted-foreground/50 focus:outline-none focus:ring-2 focus:ring-primary/40 focus:border-primary/40"
                  />
                </div>
                <div>
                  <label className="block text-sm font-medium text-foreground mb-1.5">Type ID *</label>
                  <input value={form.typeId}
                    onChange={e => updateForm({ typeId: e.target.value.toLowerCase().replace(/[^a-z0-9_]/g, "") })}
                    disabled={mode === "edit"}
                    placeholder="auto_generated_from_name"
                    className="w-full px-3 py-2.5 rounded-lg bg-secondary/50 border border-border/50 text-foreground placeholder:text-muted-foreground/50 focus:outline-none focus:ring-2 focus:ring-primary/40 disabled:opacity-50 font-mono text-sm"
                  />
                </div>
              </div>

              <div>
                <label className="block text-sm font-medium text-foreground mb-1.5">Description</label>
                <textarea value={form.description}
                  onChange={e => updateForm({ description: e.target.value })}
                  rows={2}
                  placeholder="What does this document type extract? What kinds of documents does it handle?"
                  className="w-full px-3 py-2.5 rounded-lg bg-secondary/50 border border-border/50 text-foreground placeholder:text-muted-foreground/50 focus:outline-none focus:ring-2 focus:ring-primary/40 resize-none"
                />
              </div>

              <div className="grid grid-cols-2 md:grid-cols-4 gap-4">
                <div>
                  <label className="block text-sm font-medium text-foreground mb-1.5">Category</label>
                  <select value={form.category} onChange={e => updateForm({ category: e.target.value })}
                    className="w-full px-3 py-2.5 rounded-lg bg-secondary/50 border border-border/50 text-foreground focus:outline-none focus:ring-2 focus:ring-primary/40">
                    {CATEGORY_OPTIONS.map(c => <option key={c} value={c}>{c}</option>)}
                  </select>
                </div>
                <div>
                  <label className="block text-sm font-medium text-foreground mb-1.5">Icon</label>
                  <select value={form.iconName} onChange={e => updateForm({ iconName: e.target.value })}
                    className="w-full px-3 py-2.5 rounded-lg bg-secondary/50 border border-border/50 text-foreground focus:outline-none focus:ring-2 focus:ring-primary/40">
                    {ICON_OPTIONS.map(i => <option key={i} value={i}>{i}</option>)}
                  </select>
                </div>
                <div>
                  <label className="block text-sm font-medium text-foreground mb-1.5">Max File Size (MB)</label>
                  <input type="number" value={form.maxFileSizeMb}
                    onChange={e => updateForm({ maxFileSizeMb: parseInt(e.target.value) || 50 })}
                    className="w-full px-3 py-2.5 rounded-lg bg-secondary/50 border border-border/50 text-foreground focus:outline-none focus:ring-2 focus:ring-primary/40"
                  />
                </div>
                <div>
                  <label className="block text-sm font-medium text-foreground mb-1.5">Max Pages</label>
                  <input type="number" value={form.maxPages}
                    onChange={e => updateForm({ maxPages: parseInt(e.target.value) || 30 })}
                    className="w-full px-3 py-2.5 rounded-lg bg-secondary/50 border border-border/50 text-foreground focus:outline-none focus:ring-2 focus:ring-primary/40"
                  />
                </div>
              </div>

              {/* Accepted File Types */}
              <div>
                <label className="block text-sm font-medium text-foreground mb-2">Accepted File Types</label>
                <div className="flex flex-wrap gap-2">
                  {[".pdf", ".png", ".jpg", ".jpeg", ".tiff", ".docx", ".xlsx"].map(ext => (
                    <button key={ext}
                      onClick={() => {
                        const exts = form.acceptedExtensions.includes(ext)
                          ? form.acceptedExtensions.filter(e => e !== ext)
                          : [...form.acceptedExtensions, ext];
                        updateForm({ acceptedExtensions: exts });
                      }}
                      className={`px-3 py-1.5 rounded-lg text-sm font-medium transition-all border ${
                        form.acceptedExtensions.includes(ext)
                          ? "bg-primary/15 border-primary/30 text-primary"
                          : "bg-secondary/30 border-border/50 text-muted-foreground hover:border-border"
                      }`}
                    >
                      {ext}
                    </button>
                  ))}
                </div>
              </div>

              {/* Preview card */}
              <div className="border-t border-border/30 pt-4">
                <p className="text-xs text-muted-foreground mb-2">Preview</p>
                <div className="glass-panel p-4 max-w-sm">
                  <div className="flex items-center gap-3">
                    <div className="p-2 rounded-xl bg-primary/10 text-primary border border-primary/20">
                      {ICON_MAP[form.iconName] || <FileText className="h-5 w-5" />}
                    </div>
                    <div>
                      <p className="font-semibold text-foreground">{form.displayName || "Untitled"}</p>
                      <p className="text-xs text-muted-foreground">{form.category} · v{form.version}</p>
                    </div>
                  </div>
                </div>
              </div>
            </div>
          )}

          {/* ── Step 2: System Prompt ── */}
          {currentStep === "system_prompt" && (
            <div className="space-y-4">
              <div className="flex items-center justify-between">
                <div className="flex items-center gap-2">
                  <Brain className="h-5 w-5 text-primary" />
                  <h2 className="text-lg font-semibold">System Prompt</h2>
                </div>
                <span className="text-xs text-muted-foreground">
                  {form.systemPrompt.length} characters
                </span>
              </div>
              <p className="text-sm text-muted-foreground">
                Define the AI's role, rules, and domain expertise. This sets the context for every extraction.
              </p>
              <textarea value={form.systemPrompt}
                onChange={e => updateForm({ systemPrompt: e.target.value })}
                rows={20}
                placeholder={`You are an expert document data extraction specialist for ${form.displayName || "documents"}.\n\nYour task: Extract structured data from ${form.displayName || "documents"} with 100% accuracy. Return ONLY valid JSON.\n\n═══ RULES ═══\n1. Extract all fields specified in the schema\n2. Use null for fields that cannot be found\n3. Dates must be YYYY-MM-DD format\n4. For monetary amounts, extract numeric values only\n5. Include confidence scores (0.0-1.0) for key fields`}
                className="w-full px-4 py-3 rounded-lg bg-secondary/30 border border-border/50 text-foreground placeholder:text-muted-foreground/40 focus:outline-none focus:ring-2 focus:ring-primary/40 font-mono text-sm leading-relaxed resize-none custom-scrollbar"
              />
              <div className="flex items-center gap-2 text-xs text-muted-foreground">
                <Sparkles className="h-3 w-3 text-primary" />
                <span>Tip: Include domain-specific terminology, abbreviations, and validation rules for best results</span>
              </div>
            </div>
          )}

          {/* ── Step 3: Extraction Prompt ── */}
          {currentStep === "extraction_prompt" && (
            <div className="space-y-4">
              <div className="flex items-center justify-between">
                <div className="flex items-center gap-2">
                  <FileText className="h-5 w-5 text-primary" />
                  <h2 className="text-lg font-semibold">Extraction Prompt (User Prompt)</h2>
                </div>
                <span className="text-xs text-muted-foreground">
                  {form.extractionPrompt.length} characters
                </span>
              </div>
              <p className="text-sm text-muted-foreground">
                Field-by-field extraction instructions. This is sent with each document as the user message.
              </p>
              <textarea value={form.extractionPrompt}
                onChange={e => updateForm({ extractionPrompt: e.target.value })}
                rows={20}
                placeholder={`Extract all fields from this ${form.displayName || "document"} according to the JSON schema provided.\n\nExamine every page of the document carefully.\nReturn a single JSON object with all extracted fields.\n\nFor any field you cannot find, set the value to null.\nInclude a "confidence" object with confidence scores (0.0-1.0) for the main extracted fields.\n\n═══ FIELD-SPECIFIC INSTRUCTIONS ═══\n\n• field_name_1: Description of what to extract and where to find it\n• field_name_2: Special handling notes, format requirements\n• line_items: How to identify and extract tabular data`}
                className="w-full px-4 py-3 rounded-lg bg-secondary/30 border border-border/50 text-foreground placeholder:text-muted-foreground/40 focus:outline-none focus:ring-2 focus:ring-primary/40 font-mono text-sm leading-relaxed resize-none custom-scrollbar"
              />
            </div>
          )}

          {/* ── Step 4: Schema ── */}
          {currentStep === "schema" && (
            <div className="space-y-4">
              <div className="flex items-center justify-between">
                <div className="flex items-center gap-2">
                  <Code className="h-5 w-5 text-primary" />
                  <h2 className="text-lg font-semibold">JSON Output Schema</h2>
                </div>
                <span className="text-xs text-muted-foreground">
                  Defines the exact structure GPT must return
                </span>
              </div>
              <p className="text-sm text-muted-foreground">
                Azure OpenAI Structured Output schema. Must include <code className="text-primary/80 bg-primary/10 px-1 rounded">additionalProperties: false</code> and
                all <code className="text-primary/80 bg-primary/10 px-1 rounded">required</code> arrays.
              </p>
              <textarea value={form.jsonSchema}
                onChange={e => updateForm({ jsonSchema: e.target.value })}
                rows={24}
                placeholder={`{
  "type": "object",
  "properties": {
    "field_name_1": {
      "type": ["string", "null"],
      "description": "Description of this field"
    },
    "field_name_2": {
      "type": ["number", "null"]
    },
    "line_items": {
      "type": "array",
      "items": {
        "type": "object",
        "properties": {
          "description": { "type": ["string", "null"] },
          "amount": { "type": ["number", "null"] }
        },
        "required": ["description", "amount"],
        "additionalProperties": false
      }
    },
    "confidence": {
      "type": "object",
      "additionalProperties": { "type": "number" }
    }
  },
  "required": ["field_name_1", "field_name_2", "line_items", "confidence"],
  "additionalProperties": false
}`}
                className="w-full px-4 py-3 rounded-lg bg-secondary/30 border border-border/50 text-foreground placeholder:text-muted-foreground/40 focus:outline-none focus:ring-2 focus:ring-primary/40 font-mono text-sm leading-relaxed resize-none custom-scrollbar"
              />
              {/* Schema validation indicator */}
              {form.jsonSchema && (
                <div className={`flex items-center gap-2 text-xs ${
                  (() => { try { JSON.parse(form.jsonSchema); return true; } catch { return false; } })()
                    ? "text-accent" : "text-destructive"
                }`}>
                  {(() => { try { JSON.parse(form.jsonSchema); return true; } catch { return false; } })()
                    ? <><Check className="h-3.5 w-3.5" /> Valid JSON</>
                    : <><AlertTriangle className="h-3.5 w-3.5" /> Invalid JSON — fix syntax errors</>
                  }
                </div>
              )}
            </div>
          )}

          {/* ── Step 5: Settings ── */}
          {currentStep === "settings" && (
            <div className="space-y-6">
              <div className="flex items-center gap-2 mb-2">
                <Cog className="h-5 w-5 text-primary" />
                <h2 className="text-lg font-semibold">Extraction Settings</h2>
              </div>

              <div className="grid grid-cols-1 md:grid-cols-3 gap-5">
                <div>
                  <label className="block text-sm font-medium text-foreground mb-1.5">Reasoning Effort</label>
                  <select value={form.reasoningEffort} onChange={e => updateForm({ reasoningEffort: e.target.value })}
                    className="w-full px-3 py-2.5 rounded-lg bg-secondary/50 border border-border/50 text-foreground focus:outline-none focus:ring-2 focus:ring-primary/40">
                    {REASONING_OPTIONS.map(r => <option key={r} value={r}>{r}</option>)}
                  </select>
                </div>
                <div>
                  <label className="block text-sm font-medium text-foreground mb-1.5">Max Output Tokens</label>
                  <input type="number" value={form.maxTokens}
                    onChange={e => updateForm({ maxTokens: parseInt(e.target.value) || 8192 })}
                    className="w-full px-3 py-2.5 rounded-lg bg-secondary/50 border border-border/50 text-foreground focus:outline-none focus:ring-2 focus:ring-primary/40"
                  />
                </div>
                <div>
                  <label className="block text-sm font-medium text-foreground mb-1.5">Max Pages for Vision</label>
                  <input type="number" value={form.maxPagesForVision}
                    onChange={e => updateForm({ maxPagesForVision: parseInt(e.target.value) || 12 })}
                    className="w-full px-3 py-2.5 rounded-lg bg-secondary/50 border border-border/50 text-foreground focus:outline-none focus:ring-2 focus:ring-primary/40"
                  />
                </div>
              </div>

              {/* Dual Pass */}
              <div className="glass-panel p-4 space-y-3">
                <div className="flex items-center justify-between">
                  <div className="flex items-center gap-2">
                    <Shield className="h-4 w-4 text-primary" />
                    <span className="font-medium text-sm">Dual-Pass Verification</span>
                  </div>
                  <button onClick={() => updateForm({ dualPassEnabled: !form.dualPassEnabled })}
                    className={`relative w-11 h-6 rounded-full transition-colors ${
                      form.dualPassEnabled ? "bg-primary" : "bg-secondary"
                    }`}>
                    <span className={`absolute top-0.5 h-5 w-5 rounded-full bg-white transition-transform ${
                      form.dualPassEnabled ? "translate-x-5.5 left-0" : "left-0.5"
                    }`} />
                  </button>
                </div>
                {form.dualPassEnabled && (
                  <div>
                    <label className="block text-xs text-muted-foreground mb-1">
                      Critical fields (comma-separated) — dual-pass triggers when these have low confidence
                    </label>
                    <input value={form.dualPassCriticalFields.join(", ")}
                      onChange={e => updateForm({ dualPassCriticalFields: e.target.value.split(",").map(s => s.trim()).filter(Boolean) })}
                      placeholder="e.g. total_amount, vendor_name, invoice_number"
                      className="w-full px-3 py-2 rounded-lg bg-secondary/30 border border-border/50 text-sm text-foreground placeholder:text-muted-foreground/40 focus:outline-none focus:ring-2 focus:ring-primary/40"
                    />
                  </div>
                )}
              </div>

              {/* Excel Export */}
              <div className="flex items-center justify-between glass-panel p-4">
                <div className="flex items-center gap-2">
                  <FileSpreadsheet className="h-4 w-4 text-accent" />
                  <span className="font-medium text-sm">Excel Export Enabled</span>
                </div>
                <button onClick={() => updateForm({ excelExportEnabled: !form.excelExportEnabled })}
                  className={`relative w-11 h-6 rounded-full transition-colors ${
                    form.excelExportEnabled ? "bg-accent" : "bg-secondary"
                  }`}>
                  <span className={`absolute top-0.5 h-5 w-5 rounded-full bg-white transition-transform ${
                    form.excelExportEnabled ? "translate-x-5.5 left-0" : "left-0.5"
                  }`} />
                </button>
              </div>

              {/* Version */}
              <div className="max-w-xs">
                <label className="block text-sm font-medium text-foreground mb-1.5">Version</label>
                <input value={form.version}
                  onChange={e => updateForm({ version: e.target.value })}
                  className="w-full px-3 py-2.5 rounded-lg bg-secondary/50 border border-border/50 text-foreground focus:outline-none focus:ring-2 focus:ring-primary/40"
                />
              </div>
            </div>
          )}

          {/* ── Step 6: Test ── */}
          {currentStep === "test" && (
            <div className="space-y-5">
              <div className="flex items-center gap-2 mb-2">
                <TestTube className="h-5 w-5 text-primary" />
                <h2 className="text-lg font-semibold">Test Extraction</h2>
              </div>
              <p className="text-sm text-muted-foreground">
                Upload a sample document to test your prompts and schema. Iterate until the output is right.
              </p>

              {/* Upload zone */}
              <div className="border-2 border-dashed border-border/50 rounded-xl p-8 text-center hover:border-primary/40 transition-colors">
                <input type="file" id="test-file"
                  accept={form.acceptedExtensions.map(e => e.startsWith(".") ? e : `.${e}`).join(",")}
                  onChange={e => { if (e.target.files?.[0]) setTestFile(e.target.files[0]); }}
                  className="hidden"
                />
                <label htmlFor="test-file" className="cursor-pointer">
                  <UploadIcon className="h-8 w-8 text-muted-foreground mx-auto mb-3" />
                  {testFile ? (
                    <div className="flex items-center justify-center gap-2">
                      <FileText className="h-4 w-4 text-primary" />
                      <span className="text-sm font-medium text-foreground">{testFile.name}</span>
                      <button onClick={e => { e.preventDefault(); setTestFile(null); setTestResult(null); }}
                        className="p-0.5 rounded hover:bg-destructive/10 text-muted-foreground hover:text-destructive">
                        <X className="h-3.5 w-3.5" />
                      </button>
                    </div>
                  ) : (
                    <p className="text-sm text-muted-foreground">Drop a sample file or click to browse</p>
                  )}
                </label>
              </div>

              {testFile && (
                <button onClick={handleTestExtract} disabled={testing}
                  className="flex items-center gap-2 px-5 py-2.5 rounded-xl bg-primary text-primary-foreground font-medium text-sm hover:bg-primary/90 transition-all disabled:opacity-50 btn-glow">
                  {testing ? <Loader2 className="h-4 w-4 animate-spin" /> : <Play className="h-4 w-4" />}
                  {testing ? "Extracting..." : "Run Test Extraction"}
                </button>
              )}

              {/* Test Results */}
              {testResult && (
                <div className="space-y-3">
                  <div className={`flex items-center gap-2 p-3 rounded-lg ${
                    testResult.status === "Success" ? "bg-accent/10 text-accent border border-accent/20"
                      : testResult.status === "PartialSuccess" ? "bg-warning/10 text-warning border border-warning/20"
                        : "bg-destructive/10 text-destructive border border-destructive/20"
                  }`}>
                    {testResult.status === "Success" ? <Check className="h-4 w-4" />
                      : testResult.status === "PartialSuccess" ? <AlertTriangle className="h-4 w-4" />
                        : <X className="h-4 w-4" />
                    }
                    <span className="text-sm font-medium">
                      {testResult.status} — Confidence: {(testResult.validation?.confidence_score * 100).toFixed(0)}%
                    </span>
                    {testResult.metadata?.processing_time_ms && (
                      <span className="text-xs ml-auto opacity-70">
                        {(testResult.metadata.processing_time_ms / 1000).toFixed(1)}s · {testResult.metadata.total_tokens_used} tokens
                      </span>
                    )}
                  </div>

                  {/* Extracted data */}
                  {testResult.data && (
                    <div>
                      <p className="text-xs font-medium text-muted-foreground mb-1">Extracted Data</p>
                      <pre className="p-4 rounded-lg bg-secondary/30 border border-border/50 text-xs text-foreground/90 overflow-auto max-h-[400px] font-mono custom-scrollbar">
                        {JSON.stringify(testResult.data, null, 2)}
                      </pre>
                    </div>
                  )}

                  {/* Validation messages */}
                  {testResult.validation?.messages && testResult.validation.messages.length > 0 && (
                    <div>
                      <p className="text-xs font-medium text-muted-foreground mb-1">Validation Messages</p>
                      <div className="space-y-1">
                        {testResult.validation.messages.map((msg, i) => (
                          <div key={i} className={`px-3 py-2 rounded-lg text-xs ${
                            msg.severity === "Error" ? "bg-destructive/10 text-destructive"
                              : msg.severity === "Warning" ? "bg-warning/10 text-warning"
                                : "bg-primary/10 text-primary"
                          }`}>
                            <span className="font-medium">{msg.field}:</span> {msg.message}
                          </div>
                        ))}
                      </div>
                    </div>
                  )}

                  {/* Error */}
                  {testResult.error && (
                    <div className="p-3 rounded-lg bg-destructive/10 border border-destructive/20 text-destructive text-sm">
                      {testResult.error.message}
                      {testResult.error.details && (
                        <pre className="mt-2 text-xs opacity-70 whitespace-pre-wrap">{testResult.error.details}</pre>
                      )}
                    </div>
                  )}
                </div>
              )}
            </div>
          )}
        </motion.div>
      </AnimatePresence>

      {/* Navigation buttons */}
      <div className="flex items-center justify-between">
        <button onClick={goPrev} disabled={stepIndex === 0}
          className="flex items-center gap-2 px-4 py-2 rounded-lg border border-border/50 text-muted-foreground hover:bg-secondary/50 transition-colors disabled:opacity-30 disabled:cursor-not-allowed">
          <ChevronLeft className="h-4 w-4" /> Previous
        </button>
        <div className="text-xs text-muted-foreground">
          Step {stepIndex + 1} of {STEPS.length}
        </div>
        {stepIndex < STEPS.length - 1 ? (
          <button onClick={goNext}
            className="flex items-center gap-2 px-4 py-2 rounded-lg bg-primary/10 text-primary hover:bg-primary/20 transition-colors">
            Next <ChevronRight className="h-4 w-4" />
          </button>
        ) : (
          <button onClick={handleSave} disabled={saving || !form.typeId || !form.displayName}
            className="flex items-center gap-2 px-5 py-2 rounded-lg bg-primary text-primary-foreground hover:bg-primary/90 transition-colors disabled:opacity-40 btn-glow">
            {saving ? <Loader2 className="h-4 w-4 animate-spin" /> : <Save className="h-4 w-4" />}
            {mode === "create" ? "Create Document Type" : "Save Changes"}
          </button>
        )}
      </div>
    </div>
  );
}
