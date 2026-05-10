import { motion } from "framer-motion";
import { FileCheck, Brain, AlertTriangle, XCircle } from "lucide-react";
import { Tooltip, TooltipContent, TooltipProvider, TooltipTrigger } from "@/components/ui/tooltip";

const iconMap = { FileCheck, Brain, AlertTriangle, XCircle };

interface KPICardProps {
  label: string;
  value: number;
  unit?: string;
  icon: keyof typeof iconMap;
  trend: string;
  status: "success" | "warning" | "error";
  sparkline: number[];
  index: number;
}

const statusColors = {
  success: "text-accent",
  warning: "text-warning",
  error: "text-destructive",
};

const statusGlowClass = {
  success: "glass-panel-success",
  warning: "glass-panel-warning",
  error: "glass-panel-error",
};

const contextText = {
  success: "Based on last 200 documents",
  warning: "Requires attention",
  error: "Immediate action needed",
};

function MiniSparkline({ data, status }: { data: number[]; status: string }) {
  const max = Math.max(...data);
  const min = Math.min(...data);
  const range = max - min || 1;
  const h = 28;
  const w = 72;
  
  const strokeColor = status === "success" ? "hsl(160,70%,45%)" : status === "warning" ? "hsl(38,92%,55%)" : "hsl(0,72%,55%)";
  const fillColor = status === "success" ? "hsl(160,70%,45%)" : status === "warning" ? "hsl(38,92%,55%)" : "hsl(0,72%,55%)";
  
  const points = data.map((v, i) => {
    const x = (i / (data.length - 1)) * w;
    const y = h - ((v - min) / range) * (h - 4) - 2;
    return `${x},${y}`;
  });
  
  // Create smooth path
  const pathPoints = data.map((v, i) => ({
    x: (i / (data.length - 1)) * w,
    y: h - ((v - min) / range) * (h - 4) - 2,
  }));
  
  let d = `M${pathPoints[0].x},${pathPoints[0].y}`;
  for (let i = 1; i < pathPoints.length; i++) {
    const prev = pathPoints[i - 1];
    const curr = pathPoints[i];
    const cpx = (prev.x + curr.x) / 2;
    d += ` C${cpx},${prev.y} ${cpx},${curr.y} ${curr.x},${curr.y}`;
  }
  
  const areaD = d + ` L${w},${h} L0,${h} Z`;

  return (
    <svg width={w} height={h} className="opacity-70">
      <defs>
        <linearGradient id={`grad-${status}`} x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%" stopColor={fillColor} stopOpacity="0.2" />
          <stop offset="100%" stopColor={fillColor} stopOpacity="0" />
        </linearGradient>
      </defs>
      <path d={areaD} fill={`url(#grad-${status})`} />
      <path d={d} fill="none" stroke={strokeColor} strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" />
    </svg>
  );
}

export function KPICard({ label, value, unit, icon, trend, status, sparkline, index }: KPICardProps) {
  const Icon = iconMap[icon];

  return (
    <TooltipProvider>
      <Tooltip>
        <TooltipTrigger asChild>
          <motion.div
            initial={{ opacity: 0, y: 16 }}
            animate={{ opacity: 1, y: 0 }}
            transition={{ delay: index * 0.08, duration: 0.5 }}
            className={`glass-panel-hover ${statusGlowClass[status]} group p-5 flex flex-col gap-3 cursor-default`}
          >
            <div className="flex items-center justify-between">
              <div className={`icon-glow ${status === "warning" ? "!bg-warning/12 !shadow-[0_0_16px_-2px_hsl(var(--warning)/0.3)]" : status === "error" ? "!bg-destructive/12 !shadow-[0_0_16px_-2px_hsl(var(--destructive)/0.3)]" : ""}`}>
                <Icon className={`h-4 w-4 ${statusColors[status]}`} />
              </div>
              <MiniSparkline data={sparkline} status={status} />
            </div>
            <div>
              <div className="flex items-baseline gap-1">
                <span className="text-3xl font-bold tracking-tight text-foreground">{value}</span>
                {unit && <span className="text-lg font-semibold text-muted-foreground">{unit}</span>}
              </div>
              <p className="text-sm text-muted-foreground mt-0.5">{label}</p>
            </div>
            <div className="flex items-center justify-between">
              <span className={`text-xs font-medium ${statusColors[status]}`}>{trend}</span>
              <span className="text-[10px] text-muted-foreground/60 opacity-0 group-hover:opacity-100 transition-opacity">{contextText[status]}</span>
            </div>
          </motion.div>
        </TooltipTrigger>
        <TooltipContent side="bottom" className="text-xs">
          {contextText[status]}
        </TooltipContent>
      </Tooltip>
    </TooltipProvider>
  );
}
