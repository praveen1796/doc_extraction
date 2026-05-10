import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { BrowserRouter, Route, Routes } from "react-router-dom";
import { Toaster as Sonner } from "@/components/ui/sonner";
import { Toaster } from "@/components/ui/toaster";
import { TooltipProvider } from "@/components/ui/tooltip";
import { ExtractionProvider } from "@/hooks/useExtractionResult";
import Index from "./pages/Index.tsx";
import Upload from "./pages/Upload.tsx";
import DocTypeStudio from "./pages/DocTypeStudio.tsx";
import DocumentWorkspace from "./pages/DocumentWorkspace.tsx";
import WellTwin from "./pages/WellTwin.tsx";
import NotFound from "./pages/NotFound.tsx";

const queryClient = new QueryClient();

const App = () => (
  <QueryClientProvider client={queryClient}>
    <TooltipProvider>
      <ExtractionProvider>
        <Toaster />
        <Sonner />
        <BrowserRouter>
          <Routes>
            <Route path="/" element={<Index />} />
            <Route path="/upload" element={<Upload />} />
            <Route path="/studio" element={<DocTypeStudio />} />
            <Route path="/workspace" element={<DocumentWorkspace />} />
            <Route path="/well-twin" element={<WellTwin />} />
            <Route path="*" element={<NotFound />} />
          </Routes>
        </BrowserRouter>
      </ExtractionProvider>
    </TooltipProvider>
  </QueryClientProvider>
);

export default App;
