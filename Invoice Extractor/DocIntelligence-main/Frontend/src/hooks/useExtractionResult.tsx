import { createContext, useContext, useState, type ReactNode } from "react";
import type { ExtractionResponse } from "@/services/api";

interface ExtractionState {
  // The raw file to extract (set by Upload, consumed by Workspace)
  file: File | null;
  documentType: string;
  // The extraction result (set by Workspace after API call completes)
  result: ExtractionResponse | null;
  fileName: string;
  // Whether the file is a built-in sample (skip API call, use demo data)
  isSample: boolean;
  // Actions
  stageFile: (file: File, documentType: string, isSample?: boolean) => void;
  setResult: (result: ExtractionResponse, fileName: string) => void;
  clear: () => void;
}

const ExtractionContext = createContext<ExtractionState>({
  file: null,
  documentType: "invoice",
  result: null,
  fileName: "",
  isSample: false,
  stageFile: () => {},
  setResult: () => {},
  clear: () => {},
});

export function ExtractionProvider({ children }: { children: ReactNode }) {
  const [file, setFile] = useState<File | null>(null);
  const [documentType, setDocumentType] = useState("invoice");
  const [result, setResultState] = useState<ExtractionResponse | null>(null);
  const [fileName, setFileName] = useState("");
  const [isSample, setIsSample] = useState(false);

  return (
    <ExtractionContext.Provider
      value={{
        file,
        documentType,
        result,
        fileName,
        isSample,
        stageFile: (f, dt, sample = false) => {
          console.log("[DocIQ Context] stageFile called:", f.name, "type:", dt, "sample:", sample);
          setFile(f);
          setDocumentType(dt);
          setFileName(f.name);
          setResultState(null);
          setIsSample(sample);
        },
        setResult: (r, fn) => {
          console.log("[DocIQ Context] setResult called:", fn, "status:", r.status);
          setResultState(r);
          setFileName(fn);
        },
        clear: () => {
          setFile(null);
          setDocumentType("invoice");
          setResultState(null);
          setFileName("");
          setIsSample(false);
        },
      }}
    >
      {children}
    </ExtractionContext.Provider>
  );
}

export function useExtractionResult() {
  return useContext(ExtractionContext);
}
