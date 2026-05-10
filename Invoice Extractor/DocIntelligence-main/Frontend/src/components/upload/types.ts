import type { ClassificationResult } from "@/services/classifier";

export type FileStage = "classifying" | "ready" | "extracting" | "done" | "error";

export interface StagedFile {
  id: string;
  file: File;
  stage: FileStage;
  classification: ClassificationResult | null;
  typeOverride: string | null;       // user override of auto-classification
  previewUrl: string | null;         // object URL for image/PDF thumb
  progress: number;                  // 0–100 during extraction
  progressMessage: string;
  error: string | null;
}

export function createStagedFile(file: File): StagedFile {
  return {
    id: crypto.randomUUID(),
    file,
    stage: "classifying",
    classification: null,
    typeOverride: null,
    previewUrl: null,
    progress: 0,
    progressMessage: "",
    error: null,
  };
}
