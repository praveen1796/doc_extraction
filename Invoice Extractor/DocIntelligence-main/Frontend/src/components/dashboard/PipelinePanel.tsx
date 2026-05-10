import { motion } from "framer-motion";
import { Upload, Cog, FileSearch, CheckCircle2, PackageCheck, Activity } from "lucide-react";
import { Tooltip, TooltipContent, TooltipProvider, TooltipTrigger } from "@/components/ui/tooltip";

const stageIcons = [Upload, Cog, FileSearch, CheckCircle2, PackageCheck];

const stageTooltips = [
  { avgTime: "0.4s", errorRate: "0.2%", lastFile: "INV-2024-0849.pdf" },
  { avgTime: "0.6s", errorRate: "0.5%", lastFile: "PO-NOV-3322.pdf" },
  { avgTime: "1.2s", errorRate: "1.1%", lastFile: "TS-WK48-ALPHA.pdf" },
  { avgTime: "0.8s", errorRate: "0.8%", lastFile: "TM-2024-1102.pdf" },
  { avgTime: "0.2s", errorRate: "0.0%", lastFile: "INV-2024-0848.pdf" },
];

interface Stage {
  name: string;
  count: number;
  status: "active" | "processing" | "pending" | "done";
}

export function PipelinePanel({ stages }: { stages: Stage[] }) {
  return (
    <TooltipProvider>
      <motion.div
        initial={{ opacity: 0, y: 16 }}
        animate={{ opacity: 1, y: 0 }}
        transition={{ delay: 0.3, duration: 0.5 }}
        className="glass-panel p-6"
      >
        <div className="flex items-center justify-between mb-6">
          <div>
            <h2 className="text-lg font-semibold text-foreground">Live Pipeline</h2>
            <p className="text-sm text-muted-foreground">Real-time document processing flow</p>
          </div>
          <div className="flex items-center gap-4">
            <div className="hidden sm:flex items-center gap-2 text-xs text-muted-foreground">
              <Activity className="h-3.5 w-3.5 text-primary" />
              <span className="font-semibold text-foreground">12</span> docs/sec
            </div>
            <div className="flex items-center gap-2 text-xs font-medium text-accent">
              <span className="status-dot-live" />
              SSE Connected
            </div>
          </div>
        </div>

        <div className="flex items-center justify-between gap-2">
          {stages.map((stage, i) => {
            const Icon = stageIcons[i];
            const isProcessing = stage.status === "processing";
            const isActive = stage.status === "active" || isProcessing;
            const isDone = stage.status === "done";
            const tooltip = stageTooltips[i];

            return (
              <div key={stage.name} className="flex items-center flex-1">
                <Tooltip>
                  <TooltipTrigger asChild>
                    <div className={`flex flex-col items-center gap-2 flex-1 cursor-default ${isProcessing ? "shimmer" : ""}`}>
                      <div className={`
                        relative flex items-center justify-center w-14 h-14 rounded-xl border transition-all duration-300
                        ${isProcessing ? "border-primary/50 bg-primary/10 glow-cyan" : ""}
                        ${isActive && !isProcessing ? "border-primary/30 bg-primary/5" : ""}
                        ${isDone ? "border-accent/30 bg-accent/5" : ""}
                        ${stage.status === "pending" ? "border-border bg-muted/30" : ""}
                      `}>
                        <Icon className={`h-5 w-5 ${isProcessing ? "text-primary" : isActive ? "text-primary/70" : isDone ? "text-accent" : "text-muted-foreground"}`} />
                        {isProcessing && (
                          <div className="absolute inset-0 rounded-xl border border-primary/30 animate-ping opacity-20" />
                        )}
                      </div>
                      <div className="text-center">
                        <p className={`text-xs font-semibold ${isProcessing ? "text-primary" : isActive ? "text-foreground" : isDone ? "text-accent" : "text-muted-foreground"}`}>{stage.name}</p>
                        <p className={`text-lg font-bold ${isProcessing ? "text-primary" : isDone ? "text-accent" : "text-foreground"}`}>{stage.count}</p>
                      </div>
                    </div>
                  </TooltipTrigger>
                  <TooltipContent side="bottom" className="text-xs space-y-1 p-3">
                    <p className="font-semibold text-foreground">{stage.name} Stage</p>
                    <p className="text-muted-foreground">Avg time: <span className="text-foreground">{tooltip.avgTime}</span></p>
                    <p className="text-muted-foreground">Error rate: <span className="text-foreground">{tooltip.errorRate}</span></p>
                    <p className="text-muted-foreground">Last: <span className="text-foreground">{tooltip.lastFile}</span></p>
                  </TooltipContent>
                </Tooltip>
                {i < stages.length - 1 && (
                  <div className={`relative h-px flex-shrink-0 w-8 mx-1 overflow-hidden ${i < 2 ? "bg-primary/15" : "bg-border"}`}>
                    {i < 2 && <div className="absolute inset-0 flow-line" />}
                  </div>
                )}
              </div>
            );
          })}
        </div>
      </motion.div>
    </TooltipProvider>
  );
}
