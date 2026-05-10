export const mockKPIs = [
  { label: "Processed Today", value: 247, icon: "FileCheck", trend: "+12% vs yesterday", status: "success" as const, sparkline: [18, 22, 19, 28, 31, 24, 35] },
  { label: "Avg Confidence", value: 94.7, unit: "%", icon: "Brain", trend: "Stable", status: "success" as const, sparkline: [93, 94, 95, 94, 95, 94, 95] },
  { label: "Need Review", value: 12, icon: "AlertTriangle", trend: "3 urgent", status: "warning" as const, sparkline: [8, 6, 10, 14, 9, 11, 12] },
  { label: "Failed", value: 3, icon: "XCircle", trend: "Template drift", status: "error" as const, sparkline: [1, 0, 2, 1, 0, 3, 3] },
];

export const mockPipelineStages = [
  { name: "Upload", count: 4, status: "active" as const },
  { name: "Pre-process", count: 2, status: "active" as const },
  { name: "Extract", count: 7, status: "processing" as const },
  { name: "Validate", count: 3, status: "pending" as const },
  { name: "Complete", count: 231, status: "done" as const },
];

export const mockExtractions = [
  { id: "1", file: "INV-2024-0847.pdf", type: "Invoice" as const, confidence: 98.2, issues: 0, time: "1.2s", status: "success" as const, vendor: "Acme Corp" },
  { id: "2", file: "PO-NOV-3321.pdf", type: "Purchase Order" as const, confidence: 91.4, issues: 2, time: "2.8s", status: "warning" as const, vendor: "Nova Industries" },
  { id: "3", file: "TS-WK48-ALPHA.pdf", type: "Tour Sheet" as const, confidence: 96.1, issues: 0, time: "1.5s", status: "success" as const, vendor: "Alpha Tours" },
  { id: "4", file: "TM-2024-1102.pdf", type: "Timesheet" as const, confidence: 87.3, issues: 3, time: "3.1s", status: "error" as const, vendor: "BuildRight LLC" },
  { id: "5", file: "INV-2024-0848.pdf", type: "Invoice" as const, confidence: 99.1, issues: 0, time: "0.9s", status: "success" as const, vendor: "Summit Supply" },
  { id: "6", file: "PO-NOV-3322.pdf", type: "Purchase Order" as const, confidence: 93.8, issues: 1, time: "2.3s", status: "warning" as const, vendor: "Nova Industries" },
  { id: "7", file: "INV-2024-0849.pdf", type: "Invoice" as const, confidence: 95.6, issues: 0, time: "1.1s", status: "success" as const, vendor: "Meridian Tech" },
];

export const mockInsights = [
  { id: "1", severity: "critical" as const, title: "Duplicate Invoice Detected", description: "INV-2024-0847 matches INV-2024-0612 with 97% similarity. Potential double billing from Acme Corp.", actions: ["Investigate", "Escalate"] },
  { id: "2", severity: "warning" as const, title: "Spending Spike Detected", description: "Nova Industries volume up 340% this week. 8 POs submitted in 48 hours vs 2/week average.", actions: ["Review", "Investigate"] },
  { id: "3", severity: "info" as const, title: "Low Confidence Cluster", description: "4 timesheets from BuildRight LLC averaging 84% confidence. Possible template change.", actions: ["Retrain", "Review"] },
  { id: "4", severity: "warning" as const, title: "Missing Approvals", description: "3 purchase orders over $10K lack secondary approval signatures.", actions: ["Escalate", "Review"] },
];

export const mockDocTypes = [
  { type: "Invoices", count: 142, percentage: 57, color: "primary" as const },
  { type: "Purchase Orders", count: 58, percentage: 23, color: "purple" as const },
  { type: "Tour Sheets", count: 31, percentage: 13, color: "accent" as const },
  { type: "Timesheets", count: 16, percentage: 7, color: "warning" as const },
];

export const mockVolumeData = [
  { day: "Mon", Invoices: 32, "Purchase Orders": 12, "Tour Sheets": 8, Timesheets: 4 },
  { day: "Tue", Invoices: 28, "Purchase Orders": 9, "Tour Sheets": 5, Timesheets: 3 },
  { day: "Wed", Invoices: 35, "Purchase Orders": 14, "Tour Sheets": 6, Timesheets: 2 },
  { day: "Thu", Invoices: 22, "Purchase Orders": 8, "Tour Sheets": 4, Timesheets: 3 },
  { day: "Fri", Invoices: 38, "Purchase Orders": 11, "Tour Sheets": 7, Timesheets: 2 },
  { day: "Sat", Invoices: 15, "Purchase Orders": 3, "Tour Sheets": 1, Timesheets: 1 },
  { day: "Sun", Invoices: 12, "Purchase Orders": 1, "Tour Sheets": 0, Timesheets: 1 },
];

export const mockHealth = [
  { label: "Avg Processing Time", value: "1.8s", trend: "-0.3s", trendDir: "down" as const },
  { label: "Tokens per Document", value: "2,847", trend: "+120", trendDir: "up" as const },
  { label: "Dual-Pass Rate", value: "14%", trend: "-2%", trendDir: "down" as const },
  { label: "Uptime", value: "99.97%", trend: "30 days", trendDir: "stable" as const },
];
