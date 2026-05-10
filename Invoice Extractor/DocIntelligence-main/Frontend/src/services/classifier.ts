// ═══════════════════════════════════════════════════════════════════
//  Auto-classification logic
//  Client-side heuristic before backend extraction confirms it.
//  Maps to DocumentTypeRegistry ids: invoice, purchase_order, timesheet, toursheet
// ═══════════════════════════════════════════════════════════════════

export interface ClassificationResult {
  type: string;
  displayName: string;
  confidence: number; // 0–1
  reason: string;
  iconName: string;
  category: string;
}

const DOC_TYPES = [
  {
    id: "invoice",
    displayName: "Invoice",
    patterns: [/inv(oice)?/i, /bill/i, /statement/i, /receipt/i, /factura/i, /cuenta/i],
    keywords: ["total", "amount due", "invoice number", "bill to", "subtotal", "tax"],
    iconName: "receipt",
    category: "Finance",
  },
  {
    id: "purchase_order",
    displayName: "Purchase Order",
    patterns: [/p\.?o\.?/i, /purchase.?order/i, /orden.?de.?compra/i],
    keywords: ["purchase order", "delivery date", "ship to", "terms", "requisition"],
    iconName: "shopping-cart",
    category: "Procurement",
  },
  {
    id: "timesheet",
    displayName: "Timesheet",
    patterns: [/time.?sheet/i, /t\.?s\.?/i, /hours/i, /attendance/i, /hoja.?de.?tiempo/i],
    keywords: ["hours worked", "overtime", "regular hours", "employee", "pay period"],
    iconName: "clock",
    category: "HR",
  },
  {
    id: "toursheet",
    displayName: "Tour Sheet",
    patterns: [/tour.?sheet/i, /rig.?report/i, /daily.?report/i, /field.?report/i],
    keywords: ["rig", "well", "depth", "drilling", "mud weight", "bit", "formation"],
    iconName: "clipboard",
    category: "Operations",
  },
  {
    id: "well_plan",
    displayName: "Well Plan",
    patterns: [/well.?plan/i, /drill(ing)?.?pr?o?gr?a?m/i, /batch.?drill/i, /well.?prog/i, /pad.?summary/i, /programa.?(de|operativo).?per?for?a?ci[oó]n/i, /programa.?operativo/i, /pre.?spud/i, /drilling.?(outline|procedure|order)/i, /vertical|horizontal|directional/i],
    keywords: ["well plan", "drilling program", "formation tops", "casing program", "BHA", "total depth", "lateral length", "wellbore", "programa de perforación", "esquema de pozo", "sección guía", "pre-spud", "drilling procedure"],
    iconName: "drill",
    category: "Field Operations",
  },
];

export function classifyByFilename(filename: string): ClassificationResult {
  const lower = filename.toLowerCase();

  for (const dt of DOC_TYPES) {
    for (const pat of dt.patterns) {
      if (pat.test(lower)) {
        return {
          type: dt.id,
          displayName: dt.displayName,
          confidence: 0.82,
          reason: `Filename matches ${dt.displayName.toLowerCase()} pattern`,
          iconName: dt.iconName,
          category: dt.category,
        };
      }
    }
  }

  // Default to invoice (most common) with low confidence
  return {
    type: "invoice",
    displayName: "Invoice",
    confidence: 0.4,
    reason: "Default classification — verify document type",
    iconName: "receipt",
    category: "Finance",
  };
}

export function getAllDocTypes() {
  return DOC_TYPES.map((d) => ({
    id: d.id,
    displayName: d.displayName,
    iconName: d.iconName,
    category: d.category,
  }));
}
