import { motion } from "framer-motion";
import { TrendingDown, TrendingUp, Minus, Sparkles } from "lucide-react";

interface HealthItem {
  label: string;
  value: string;
  trend: string;
  trendDir: "up" | "down" | "stable";
}

const trendIcons = { up: TrendingUp, down: TrendingDown, stable: Minus };
const trendColors = { up: "text-warning", down: "text-accent", stable: "text-muted-foreground" };

export function HealthPanel({ data }: { data: HealthItem[] }) {
  return (
    <motion.div
      initial={{ opacity: 0, y: 16 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ delay: 0.55, duration: 0.5 }}
      className="glass-panel p-5"
    >
      <h2 className="text-lg font-semibold text-foreground mb-1">Processing Health</h2>
      <p className="text-sm text-muted-foreground mb-5">Operational intelligence metrics</p>

      <div className="grid grid-cols-2 gap-3">
        {data.map((item) => {
          const TrendIcon = trendIcons[item.trendDir];
          return (
            <div key={item.label} className="rounded-lg border border-border/50 bg-muted/20 p-3.5 hover:border-primary/20 transition-colors">
              <p className="text-xs text-muted-foreground mb-1.5">{item.label}</p>
              <p className="text-xl font-bold text-foreground">{item.value}</p>
              <div className={`flex items-center gap-1 mt-1 text-xs font-medium ${trendColors[item.trendDir]}`}>
                <TrendIcon className="h-3 w-3" />
                {item.trend}
              </div>
            </div>
          );
        })}
      </div>

      {/* AI narration */}
      <div className="mt-4 flex items-start gap-2 rounded-lg bg-primary/5 border border-primary/15 p-3">
        <Sparkles className="h-3.5 w-3.5 text-primary mt-0.5 flex-shrink-0" />
        <p className="text-xs text-muted-foreground leading-relaxed">
          Processing time improved due to smaller document sizes and fewer validation retries this period.
        </p>
      </div>
    </motion.div>
  );
}
