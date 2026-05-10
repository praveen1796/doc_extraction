import { motion, AnimatePresence } from "framer-motion";
import { Eye, Bot, GitCompare, Download, AlertTriangle, Copy, Sparkles, ChevronDown, ChevronRight } from "lucide-react";
import { useState } from "react";
import { useNavigate } from "react-router-dom";

interface Extraction {
  id: string;
  file: string;
  type: "Invoice" | "Purchase Order" | "Tour Sheet" | "Timesheet";
  confidence: number;
  issues: number;
  time: string;
  status: "success" | "warning" | "error";
  vendor: string;
}

const typeClasses: Record<string, string> = {
  "Invoice": "type-pill-invoice",
  "Purchase Order": "type-pill-po",
  "Tour Sheet": "type-pill-tour",
  "Timesheet": "type-pill-timesheet",
};

const statusDot: Record<string, string> = {
  success: "bg-accent",
  warning: "bg-warning",
  error: "bg-destructive",
};

const rowInsights: Record<string, { fields: string[]; aiNote: string; flags: string[] }> = {
  "1": { fields: ["Invoice #: 2024-0847", "Amount: $12,450.00", "Due: Mar 15"], aiNote: "Potential duplicate — matches INV-2024-0612 with 97% similarity.", flags: ["duplicate"] },
  "2": { fields: ["PO #: NOV-3321", "Amount: $8,200.00", "Terms: Net 30"], aiNote: "2 field mismatches detected. Line item quantities differ from contract.", flags: ["anomaly"] },
  "3": { fields: ["Tour: WK48-ALPHA", "Duration: 4 days", "Stops: 12"], aiNote: "No anomalies detected. Matches historical pattern.", flags: [] },
  "4": { fields: ["Employee: J. Mitchell", "Hours: 42.5", "Period: Nov 1-15"], aiNote: "Template change detected. 3 fields could not be mapped. Retrain recommended.", flags: ["ai-flagged"] },
  "5": { fields: ["Invoice #: 2024-0848", "Amount: $3,890.00", "Due: Mar 22"], aiNote: "No anomalies detected. High confidence extraction.", flags: [] },
  "6": { fields: ["PO #: NOV-3322", "Amount: $15,600.00", "Terms: Net 45"], aiNote: "Amount exceeds typical range for this vendor by 220%.", flags: ["anomaly"] },
  "7": { fields: ["Invoice #: 2024-0849", "Amount: $7,125.00", "Due: Mar 28"], aiNote: "No anomalies detected. Verified against contract.", flags: [] },
};

function ConfidenceBar({ value }: { value: number }) {
  const color = value >= 95 ? "bg-accent" : value >= 90 ? "bg-primary" : value >= 85 ? "bg-warning" : "bg-destructive";
  return (
    <div className="flex items-center gap-2">
      <span className="text-sm font-semibold text-foreground w-12">{value}%</span>
      <div className="h-1.5 w-20 rounded-full bg-muted overflow-hidden">
        <div className={`h-full rounded-full ${color} transition-all duration-500`} style={{ width: `${value}%` }} />
      </div>
    </div>
  );
}

function RowFlags({ flags }: { flags: string[] }) {
  if (flags.length === 0) return null;
  return (
    <div className="flex items-center gap-1 ml-2">
      {flags.includes("duplicate") && <Copy className="h-3 w-3 text-warning" />}
      {flags.includes("anomaly") && <AlertTriangle className="h-3 w-3 text-warning" />}
      {flags.includes("ai-flagged") && <Sparkles className="h-3 w-3 text-primary" />}
    </div>
  );
}

export function ExtractionsTable({ data }: { data: Extraction[] }) {
  const [hoveredRow, setHoveredRow] = useState<string | null>(null);
  const [expandedRow, setExpandedRow] = useState<string | null>(null);
  const navigate = useNavigate();

  return (
    <motion.div
      initial={{ opacity: 0, y: 16 }}
      animate={{ opacity: 1, y: 0 }}
      transition={{ delay: 0.4, duration: 0.5 }}
      className="glass-panel overflow-hidden"
    >
      <div className="p-5 pb-3">
        <h2 className="text-lg font-semibold text-foreground">Recent Extractions</h2>
        <p className="text-sm text-muted-foreground">Latest document processing results</p>
      </div>
      <div className="overflow-x-auto">
        <table className="w-full text-sm">
          <thead>
            <tr className="border-b border-border text-left text-xs font-medium text-muted-foreground uppercase tracking-wider">
              <th className="px-5 py-3 w-6"></th>
              <th className="px-5 py-3">File</th>
              <th className="px-5 py-3">Type</th>
              <th className="px-5 py-3">Confidence</th>
              <th className="px-5 py-3">Issues</th>
              <th className="px-5 py-3">Time</th>
              <th className="px-5 py-3 w-32"></th>
            </tr>
          </thead>
          <tbody>
            {data.map((row) => {
              const insight = rowInsights[row.id];
              const isExpanded = expandedRow === row.id;
              return (
                <>
                  <tr
                    key={row.id}
                    className="border-b border-border/50 hover:bg-glass-highlight/5 transition-colors cursor-pointer"
                    onMouseEnter={() => setHoveredRow(row.id)}
                    onMouseLeave={() => setHoveredRow(null)}
                    onClick={() => setExpandedRow(isExpanded ? null : row.id)}
                  >
                    <td className="pl-5 py-3.5">
                      {isExpanded ? (
                        <ChevronDown className="h-3.5 w-3.5 text-muted-foreground" />
                      ) : (
                        <ChevronRight className="h-3.5 w-3.5 text-muted-foreground" />
                      )}
                    </td>
                    <td className="px-5 py-3.5">
                      <div className="flex items-center gap-2.5">
                        <span className={`status-dot ${statusDot[row.status]}`} />
                        <div>
                          <div className="flex items-center">
                            <p className="font-medium text-foreground">{row.file}</p>
                            {insight && <RowFlags flags={insight.flags} />}
                          </div>
                          <p className="text-xs text-muted-foreground">{row.vendor}</p>
                        </div>
                      </div>
                    </td>
                    <td className="px-5 py-3.5"><span className={typeClasses[row.type]}>{row.type}</span></td>
                    <td className="px-5 py-3.5"><ConfidenceBar value={row.confidence} /></td>
                    <td className="px-5 py-3.5">
                      <span className={`font-medium ${row.issues === 0 ? "text-muted-foreground" : "text-warning"}`}>{row.issues}</span>
                    </td>
                    <td className="px-5 py-3.5 text-muted-foreground">{row.time}</td>
                    <td className="px-5 py-3.5">
                      <div className={`flex items-center gap-1 transition-opacity duration-200 ${hoveredRow === row.id ? "opacity-100" : "opacity-0"}`}>
                        {[
                          { Icon: Eye, tip: "Open" },
                          { Icon: Bot, tip: "Ask AI" },
                          { Icon: GitCompare, tip: "Compare" },
                          { Icon: Download, tip: "Export" },
                        ].map(({ Icon, tip }) => (
                          <button
                            key={tip}
                            className="p-1.5 rounded-md hover:bg-primary/10 text-muted-foreground hover:text-primary transition-colors"
                            onClick={(e) => { e.stopPropagation(); if (tip === "Open") navigate("/workspace"); }}
                            title={tip}
                          >
                            <Icon className="h-3.5 w-3.5" />
                          </button>
                        ))}
                      </div>
                    </td>
                  </tr>
                  <AnimatePresence>
                    {isExpanded && insight && (
                      <tr key={`${row.id}-detail`}>
                        <td colSpan={7} className="px-0">
                          <motion.div
                            initial={{ height: 0, opacity: 0 }}
                            animate={{ height: "auto", opacity: 1 }}
                            exit={{ height: 0, opacity: 0 }}
                            transition={{ duration: 0.25 }}
                            className="overflow-hidden"
                          >
                            <div className="px-12 py-4 bg-muted/10 border-b border-border/50">
                              <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
                                <div>
                                  <p className="text-xs font-semibold text-muted-foreground uppercase tracking-wider mb-2">Extracted Fields</p>
                                  <div className="space-y-1">
                                    {insight.fields.map((f) => (
                                      <p key={f} className="text-sm text-foreground">{f}</p>
                                    ))}
                                  </div>
                                </div>
                                <div>
                                  <p className="text-xs font-semibold text-muted-foreground uppercase tracking-wider mb-2">AI Insight</p>
                                  <div className="flex items-start gap-2 rounded-lg bg-primary/5 border border-primary/15 p-3">
                                    <Sparkles className="h-3.5 w-3.5 text-primary mt-0.5 flex-shrink-0" />
                                    <p className="text-sm text-muted-foreground leading-relaxed">{insight.aiNote}</p>
                                  </div>
                                </div>
                              </div>
                            </div>
                          </motion.div>
                        </td>
                      </tr>
                    )}
                  </AnimatePresence>
                </>
              );
            })}
          </tbody>
        </table>
      </div>
    </motion.div>
  );
}
