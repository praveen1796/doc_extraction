import { motion, AnimatePresence } from "framer-motion";
import { Bot } from "lucide-react";
import { useState, useEffect } from "react";

const liveInsights = [
  "Duplicate invoice detected — INV-2048 matches INV-2024-0612 (97% similarity)",
  "3 approvals pending for POs over $10K — escalation recommended",
  "Processing speed improved 18% over the last hour",
  "Vendor NOV shows unusual volume spike — 8 POs in 48 hours vs 2/week avg",
  "4 BuildRight LLC timesheets flagged for template drift",
];

export function AILiveStrip() {
  const [index, setIndex] = useState(0);

  useEffect(() => {
    const timer = setInterval(() => {
      setIndex((prev) => (prev + 1) % liveInsights.length);
    }, 4500);
    return () => clearInterval(timer);
  }, []);

  return (
    <motion.div
      initial={{ opacity: 0, y: 8 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ delay: 0.1, duration: 0.4 }}
      className="glass-panel-bright px-5 py-3"
    >
      <div className="flex items-center gap-3">
        <div className="flex-shrink-0 rounded-lg p-1.5 bg-primary/10">
          <Bot className="h-4 w-4 text-primary ai-pulse" />
        </div>
        <div className="flex items-center gap-2 min-w-0 flex-1">
          <span className="text-[10px] font-bold uppercase tracking-widest text-primary/70 flex-shrink-0">
            AI Live
          </span>
          <span className="text-border flex-shrink-0">|</span>
          <div className="relative h-5 flex-1 overflow-hidden">
            <AnimatePresence mode="wait">
              <motion.p
                key={index}
                initial={{ opacity: 0, y: 8 }}
                animate={{ opacity: 1, y: 0 }}
                exit={{ opacity: 0, y: -8 }}
                transition={{ duration: 0.35 }}
                className="text-sm text-muted-foreground absolute inset-0 truncate"
              >
                {liveInsights[index]}
              </motion.p>
            </AnimatePresence>
          </div>
        </div>
        <div className="hidden sm:flex items-center gap-1.5 flex-shrink-0">
          {liveInsights.map((_, i) => (
            <span
              key={i}
              className={`h-1 rounded-full transition-all duration-300 ${
                i === index ? "w-4 bg-primary" : "w-1 bg-muted-foreground/30"
              }`}
            />
          ))}
        </div>
      </div>
    </motion.div>
  );
}
