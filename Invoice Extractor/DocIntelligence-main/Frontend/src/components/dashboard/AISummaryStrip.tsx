import { motion } from "framer-motion";
import { Sparkles } from "lucide-react";

export function AISummaryStrip() {
  const summaries = [
    "247 documents processed successfully today",
    "3 failures linked to template drift in BuildRight LLC timesheets",
    "12 documents need review — mostly PO mismatches from Nova Industries",
    "Vendor NOV shows unusual volume spike (+340% this week)",
  ];

  return (
    <motion.div
      initial={{ opacity: 0, y: 12 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ delay: 0.15, duration: 0.5 }}
      className="glass-panel p-4"
    >
      <div className="flex items-start gap-3">
        <div className="icon-glow mt-0.5 flex-shrink-0">
          <Sparkles className="h-4 w-4 text-primary" />
        </div>
        <div className="flex-1 min-w-0">
          <p className="text-xs font-semibold text-primary mb-2 uppercase tracking-wider">AI Daily Briefing</p>
          <div className="grid grid-cols-1 md:grid-cols-2 gap-x-6 gap-y-1.5">
            {summaries.map((s, i) => (
              <p key={i} className="text-sm text-muted-foreground leading-relaxed flex items-start gap-2">
                <span className="text-primary/60 mt-1.5 flex-shrink-0">•</span>
                {s}
              </p>
            ))}
          </div>
        </div>
      </div>
    </motion.div>
  );
}
