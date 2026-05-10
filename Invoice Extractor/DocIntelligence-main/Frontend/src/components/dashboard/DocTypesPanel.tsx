import { motion } from "framer-motion";
import { TrendingUp } from "lucide-react";

interface DocType {
  type: string;
  count: number;
  percentage: number;
  color: "primary" | "purple" | "accent" | "warning";
}

const colorMap = {
  primary: { bar: "bg-primary", text: "text-primary", dot: "bg-primary" },
  purple: { bar: "bg-purple-500", text: "text-purple-400", dot: "bg-purple-500" },
  accent: { bar: "bg-accent", text: "text-accent", dot: "bg-accent" },
  warning: { bar: "bg-warning", text: "text-warning", dot: "bg-warning" },
};

export function DocTypesPanel({ data }: { data: DocType[] }) {
  const total = data.reduce((s, d) => s + d.count, 0);
  const dominant = data.reduce((a, b) => (a.count > b.count ? a : b));

  return (
    <motion.div
      initial={{ opacity: 0, y: 16 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ delay: 0.5, duration: 0.5 }}
      className="glass-panel p-5"
    >
      <h2 className="text-lg font-semibold text-foreground mb-1">Document Types</h2>
      <p className="text-sm text-muted-foreground mb-5">{total} documents this period</p>

      {/* Segmented bar */}
      <div className="flex h-4 rounded-full overflow-hidden mb-2 bg-muted">
        {data.map((d) => (
          <div
            key={d.type}
            className={`${colorMap[d.color].bar} transition-all duration-500 hover:brightness-110 cursor-pointer`}
            style={{ width: `${d.percentage}%` }}
            title={`${d.type}: ${d.count} (${d.percentage}%)`}
          />
        ))}
      </div>

      {/* Insight text */}
      <div className="flex items-center gap-4 mb-5 text-xs text-muted-foreground">
        <span>Most dominant: <span className={`font-semibold ${colorMap[dominant.color].text}`}>{dominant.type} ({dominant.percentage}%)</span></span>
        <span className="flex items-center gap-1 text-accent">
          <TrendingUp className="h-3 w-3" />
          +12% vs last week
        </span>
      </div>

      <div className="space-y-3">
        {data.map((d) => (
          <div key={d.type} className="flex items-center justify-between group cursor-pointer hover:bg-glass-highlight/5 -mx-2 px-2 py-1 rounded-lg transition-colors">
            <div className="flex items-center gap-2.5">
              <span className={`h-2.5 w-2.5 rounded-full ${colorMap[d.color].dot}`} />
              <span className="text-sm text-foreground">{d.type}</span>
            </div>
            <div className="flex items-center gap-3">
              <span className="text-sm font-semibold text-foreground">{d.count}</span>
              <span className={`text-xs font-medium ${colorMap[d.color].text}`}>{d.percentage}%</span>
            </div>
          </div>
        ))}
      </div>
    </motion.div>
  );
}
