import { motion } from "framer-motion";
import { AlertTriangle, AlertOctagon, Info } from "lucide-react";

interface Insight {
  id: string;
  severity: "critical" | "warning" | "info";
  title: string;
  description: string;
  actions: string[];
}

const severityConfig = {
  critical: { icon: AlertOctagon, border: "border-destructive/30", bg: "bg-destructive/5", iconColor: "text-destructive" },
  warning: { icon: AlertTriangle, border: "border-warning/30", bg: "bg-warning/5", iconColor: "text-warning" },
  info: { icon: Info, border: "border-primary/30", bg: "bg-primary/5", iconColor: "text-primary" },
};

const actionStyles: Record<string, string> = {
  "Investigate": "bg-primary/10 text-primary border-primary/20 hover:bg-primary/20",
  "Escalate": "bg-destructive/10 text-destructive border-destructive/20 hover:bg-destructive/20",
  "Review": "bg-warning/10 text-warning border-warning/20 hover:bg-warning/20",
  "Retrain": "bg-accent/10 text-accent border-accent/20 hover:bg-accent/20",
  "Mark Duplicate": "bg-warning/10 text-warning border-warning/20 hover:bg-warning/20",
};

export function InsightsPanel({ insights }: { insights: Insight[] }) {
  return (
    <motion.div
      initial={{ opacity: 0, y: 16 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ delay: 0.5, duration: 0.5 }}
      className="glass-panel p-5"
    >
      <div className="flex items-center justify-between mb-4">
        <div>
          <h2 className="text-lg font-semibold text-foreground">Auto Insights</h2>
          <p className="text-sm text-muted-foreground">AI-generated alerts & recommendations</p>
        </div>
        <div className="flex items-center gap-1.5 text-xs text-primary font-medium">
          <span className="h-1.5 w-1.5 rounded-full bg-primary animate-pulse" />
          Monitoring
        </div>
      </div>

      <div className="space-y-3">
        {insights.map((insight, i) => {
          const config = severityConfig[insight.severity];
          const Icon = config.icon;
          return (
            <motion.div
              key={insight.id}
              initial={{ opacity: 0, x: -8 }}
              animate={{ opacity: 1, x: 0 }}
              transition={{ delay: 0.6 + i * 0.1 }}
              className={`rounded-lg border ${config.border} ${config.bg} p-4 group hover:border-opacity-60 transition-all duration-200`}
            >
              <div className="flex items-start gap-3">
                <div className={`mt-0.5 rounded-md p-1.5 ${config.bg}`}>
                  <Icon className={`h-4 w-4 ${config.iconColor}`} />
                </div>
                <div className="flex-1 min-w-0">
                  <p className="font-semibold text-foreground text-sm">{insight.title}</p>
                  <p className="text-xs text-muted-foreground mt-1 leading-relaxed">{insight.description}</p>
                  <div className="flex items-center gap-2 mt-3">
                    {insight.actions.map((action) => (
                      <button
                        key={action}
                        className={`inline-flex items-center rounded-md border px-2.5 py-1 text-[11px] font-semibold transition-colors ${actionStyles[action] || "bg-secondary/50 text-secondary-foreground border-border hover:bg-secondary"}`}
                      >
                        {action}
                      </button>
                    ))}
                  </div>
                </div>
              </div>
            </motion.div>
          );
        })}
      </div>
    </motion.div>
  );
}
