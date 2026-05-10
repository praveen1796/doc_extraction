using System.Text.Json;
using ClosedXML.Excel;
using DocumentExtractionService.Core.Models;
using DocumentExtractionService.Core.Services;

namespace DocumentExtractionService.Api.Services;

/// <summary>
/// Professional multi-sheet Excel export for contract extraction results.
///
/// Handles BOTH schema variants:
///   A) Simplified — flat header + line_items[] array
///   B) Comprehensive — nested objects (compensation, insurance, indemnity, etc.)
///
/// SHEETS PRODUCED:
///   1. Summary          — Contract identity, parties, dates, rig, governing law
///   2. Rates & Fees     — All commercial terms flattened into one sortable table
///   3. Legal Terms      — Insurance limits, indemnity, term/termination
///   4. Exhibits         — Exhibit inventory with page ranges
///   5. Signatures       — Execution details
/// </summary>
public static class ContractExcelExporter
{
    // ── Nabors brand palette ──
    private static readonly XLColor NavyDark    = XLColor.FromArgb(18, 36, 56);
    private static readonly XLColor NaborsCyan  = XLColor.FromArgb(0, 173, 220);
    private static readonly XLColor HeaderFg    = XLColor.White;
    private static readonly XLColor SectionBg   = XLColor.FromArgb(225, 240, 250);
    private static readonly XLColor SectionFg   = XLColor.FromArgb(18, 36, 56);
    private static readonly XLColor AltRowBg    = XLColor.FromArgb(245, 248, 251);
    private static readonly XLColor LabelColor  = XLColor.FromArgb(90, 100, 115);
    private static readonly XLColor BorderLight = XLColor.FromArgb(210, 218, 226);
    private static readonly XLColor GreenOk     = XLColor.FromArgb(16, 163, 127);
    private static readonly XLColor RedWarn     = XLColor.FromArgb(200, 80, 80);

    public static ExportResult Export(ExtractionResponse result)
    {
        if (result.Data is null)
            throw new InvalidOperationException("Cannot export: extraction has no data.");

        var root = result.Data.RootElement;
        using var wb = new XLWorkbook();

        BuildSummarySheet(wb, root, result.Metadata);
        BuildRatesSheet(wb, root);
        BuildLegalSheet(wb, root);
        BuildExhibitsSheet(wb, root);
        BuildSignaturesSheet(wb, root);

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        var safeName = ExportFileNameHelper.Sanitize(result.Metadata.SourceFile);

        return new ExportResult
        {
            FileBytes = ms.ToArray(),
            ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            FileName = $"{safeName}_contract_rates.xlsx"
        };
    }

    // ═══════════════════════════════════════════════════════════════════
    //  SHEET 1: Summary
    // ═══════════════════════════════════════════════════════════════════

    private static void BuildSummarySheet(XLWorkbook wb, JsonElement root, ExtractionMetadata meta)
    {
        var ws = wb.Worksheets.Add("Summary");

        // Title bar
        var titleRange = ws.Range("A1:B1");
        titleRange.Merge();
        ws.Cell(1, 1).Value = "Contract Extraction Summary";
        ws.Cell(1, 1).Style.Font.Bold = true;
        ws.Cell(1, 1).Style.Font.FontSize = 15;
        ws.Cell(1, 1).Style.Font.FontColor = HeaderFg;
        ws.Cell(1, 1).Style.Fill.BackgroundColor = NavyDark;
        ws.Cell(1, 1).Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
        ws.Row(1).Height = 36;

        int row = 3;

        // ── Contract Identity ──
        row = SectionTitle(ws, row, "CONTRACT IDENTITY");
        row = SummaryRow(ws, row, "Contract Title", Str(root, "contract_title"));
        row = SummaryRow(ws, row, "Contract Type", Str(root, "contract_type"));
        row = SummaryRow(ws, row, "Reference Number", Str(root, "reference_number"));
        row = SummaryRow(ws, row, "IADC Form", Str(root, "iadc_form_indicator"));
        row = SummaryRow(ws, row, "Effective Date", Str(root, "effective_date"));
        row = SummaryRow(ws, row, "Commencement Date", Str(root, "commencement_date"));
        row = SummaryRow(ws, row, "Master Agreement Ref", Str(root, "master_agreement_reference"));
        row = SummaryRow(ws, row, "DocuSign Envelope ID", Str(root, "docusign_envelope_id"));

        // Reference numbers array
        if (root.TryGetProperty("reference_numbers", out var refs) && refs.ValueKind == JsonValueKind.Array)
        {
            foreach (var r in refs.EnumerateArray())
            {
                var label = StrFrom(r, "label") ?? "Reference";
                var val = StrFrom(r, "value") ?? "";
                row = SummaryRow(ws, row, label, val);
            }
        }

        row++;

        // ── Parties ──
        row = SectionTitle(ws, row, "OPERATOR");
        if (root.TryGetProperty("operator", out var op) && op.ValueKind == JsonValueKind.Object)
        {
            row = SummaryRow(ws, row, "Name", StrFrom(op, "name"));
            row = SummaryRow(ws, row, "Address", StrFrom(op, "address"));
            row = SummaryRow(ws, row, "Signer", StrFrom(op, "signer_name"));
            row = SummaryRow(ws, row, "Title", StrFrom(op, "signer_title"));
        }
        else
        {
            row = SummaryRow(ws, row, "Name", Str(root, "operator_name"));
            row = SummaryRow(ws, row, "Address", Str(root, "operator_address"));
        }
        row++;

        row = SectionTitle(ws, row, "CONTRACTOR");
        if (root.TryGetProperty("contractor", out var ct) && ct.ValueKind == JsonValueKind.Object)
        {
            row = SummaryRow(ws, row, "Name", StrFrom(ct, "name"));
            row = SummaryRow(ws, row, "Division", StrFrom(ct, "division"));
            row = SummaryRow(ws, row, "Address", StrFrom(ct, "address"));
            row = SummaryRow(ws, row, "Signer", StrFrom(ct, "signer_name"));
            row = SummaryRow(ws, row, "Title", StrFrom(ct, "signer_title"));
        }
        else
        {
            row = SummaryRow(ws, row, "Name", Str(root, "contractor_name"));
            row = SummaryRow(ws, row, "Address", Str(root, "contractor_address"));
        }
        row++;

        // ── Well & Location ──
        row = SectionTitle(ws, row, "WELL & LOCATION");
        if (root.TryGetProperty("well_location", out var wl) && wl.ValueKind == JsonValueKind.Object)
        {
            row = SummaryRow(ws, row, "Well Name", StrFrom(wl, "well_name"));
            row = SummaryRow(ws, row, "County", StrFrom(wl, "county"));
            row = SummaryRow(ws, row, "State", StrFrom(wl, "state"));
            row = SummaryRow(ws, row, "Field", StrFrom(wl, "field_name"));
            row = SummaryRow(ws, row, "Approx Depth", StrFrom(wl, "well_depth_approximate"));
            row = SummaryRow(ws, row, "Formation Target", StrFrom(wl, "formation_target"));
        }
        row++;

        // ── Rig ──
        row = SectionTitle(ws, row, "EQUIPMENT / RIG");
        if (root.TryGetProperty("equipment_rig", out var eq) && eq.ValueKind == JsonValueKind.Object)
        {
            row = SummaryRow(ws, row, "Rig Designation", StrFrom(eq, "rig_designation"));
            row = SummaryRow(ws, row, "Rig Type", StrFrom(eq, "rig_type"));
            row = SummaryRow(ws, row, "Major Equipment", StrFrom(eq, "major_equipment_summary"));
            row = SummaryRow(ws, row, "BOP Configuration", StrFrom(eq, "bop_configuration"));
        }
        else
        {
            row = SummaryRow(ws, row, "Rig Designation", Str(root, "rig_designation"));
        }
        row++;

        // ── Governing Law ──
        row = SectionTitle(ws, row, "GOVERNING LAW & COMPLIANCE");
        if (root.TryGetProperty("governing_law", out var gl) && gl.ValueKind == JsonValueKind.Object)
        {
            row = SummaryRow(ws, row, "Governing Law", StrFrom(gl, "governing_law_state"));
            row = SummaryRow(ws, row, "Venue County", StrFrom(gl, "venue_county"));
            row = SummaryRow(ws, row, "Venue State", StrFrom(gl, "venue_state"));
            row = SummaryRow(ws, row, "Independent Contractor", StrFrom(gl, "independent_contractor"));
            row = SummaryRow(ws, row, "Dispute Resolution", StrFrom(gl, "dispute_resolution_program"));
            row = SummaryRow(ws, row, "Audit Period (Years)", StrFrom(gl, "audit_rights_period_years"));
        }
        else
        {
            row = SummaryRow(ws, row, "Governing Law", Str(root, "governing_law"));
            row = SummaryRow(ws, row, "Venue", Str(root, "venue"));
        }
        row++;

        // ── Source metadata ──
        row = SectionTitle(ws, row, "EXTRACTION METADATA");
        row = SummaryRow(ws, row, "Source File", meta.SourceFile);
        row = SummaryRow(ws, row, "Pages", meta.PageCount.ToString());
        row = SummaryRow(ws, row, "Extracted At", meta.ExtractedAt.ToString("u"));
        row = SummaryRow(ws, row, "Processing Time", $"{meta.ProcessingTimeMs / 1000.0:F1}s");

        if (root.TryGetProperty("extraction_quality", out var eq2) && eq2.ValueKind == JsonValueKind.Object)
        {
            row = SummaryRow(ws, row, "Overall Confidence", StrFrom(eq2, "overall_confidence"));
            row = SummaryRow(ws, row, "OCR Dependency", StrFrom(eq2, "ocr_dependency_level"));
            var notes = StrFrom(eq2, "notes");
            if (!string.IsNullOrEmpty(notes))
                row = SummaryRow(ws, row, "Notes", notes);
        }

        // Column widths
        ws.Column(1).Width = 26;
        ws.Column(2).Width = 80;
        ws.Column(2).Style.Alignment.WrapText = true;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  SHEET 2: Rates & Fees
    // ═══════════════════════════════════════════════════════════════════

    private static void BuildRatesSheet(XLWorkbook wb, JsonElement root)
    {
        var ws = wb.Worksheets.Add("Rates & Fees");

        // Collect all rate items from any schema shape
        var items = new List<RateItem>();

        // Path A: simplified schema with line_items[]
        if (root.TryGetProperty("line_items", out var li) && li.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in li.EnumerateArray())
            {
                var desc = StrFrom(item, "description");
                if (string.IsNullOrEmpty(desc)) continue;
                items.Add(new RateItem
                {
                    Section = StrFrom(item, "section") ?? "General",
                    Description = desc,
                    Amount = AmountStr(item),
                    AmountNumeric = AmountNum(item),
                    Unit = StrFrom(item, "unit") ?? "",
                    Notes = StrFrom(item, "notes") ?? "",
                    PageRef = IntFrom(item, "page_ref")
                });
            }
        }

        // Path B: comprehensive schema — flatten nested objects into line items
        if (root.TryGetProperty("compensation", out var comp) && comp.ValueKind == JsonValueKind.Object)
            FlattenCompensation(comp, items);

        if (root.TryGetProperty("technology_packages", out var tp) && tp.ValueKind == JsonValueKind.Array)
            FlattenTechPackages(tp, items);

        if (root.TryGetProperty("additional_provisions", out var ap) && ap.ValueKind == JsonValueKind.Array)
            FlattenAdditionalProvisions(ap, items);

        if (root.TryGetProperty("payment_terms", out var pt) && pt.ValueKind == JsonValueKind.Object)
            FlattenPaymentTerms(pt, items);

        if (items.Count == 0)
        {
            ws.Cell(1, 1).Value = "No commercial terms extracted.";
            ws.Cell(1, 1).Style.Font.FontColor = LabelColor;
            return;
        }

        // ── Header row ──
        string[] headers = { "#", "Section", "Description", "Amount", "Unit", "Notes", "Pg" };
        int[] widths = { 5, 24, 52, 18, 22, 44, 5 };

        for (int c = 0; c < headers.Length; c++)
        {
            var cell = ws.Cell(1, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = HeaderFg;
            cell.Style.Fill.BackgroundColor = NavyDark;
            cell.Style.Alignment.Vertical = XLAlignmentVerticalValues.Center;
            cell.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
            cell.Style.Border.BottomBorderColor = NaborsCyan;
            ws.Column(c + 1).Width = widths[c];
        }
        // Right-align Amount header
        ws.Cell(1, 4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
        ws.Row(1).Height = 30;
        ws.SheetView.FreezeRows(1);

        // ── Data rows with section grouping ──
        int row = 2;
        int itemNum = 0;
        string currentSection = "";

        foreach (var item in items)
        {
            // Section break header
            if (!string.Equals(item.Section, currentSection, StringComparison.OrdinalIgnoreCase))
            {
                if (row > 2) row++; // gap
                var sectionRange = ws.Range(row, 1, row, headers.Length);
                ws.Cell(row, 2).Value = item.Section;
                sectionRange.Style.Font.Bold = true;
                sectionRange.Style.Font.FontSize = 11;
                sectionRange.Style.Font.FontColor = SectionFg;
                sectionRange.Style.Fill.BackgroundColor = SectionBg;
                sectionRange.Style.Border.BottomBorder = XLBorderStyleValues.Thin;
                sectionRange.Style.Border.BottomBorderColor = NaborsCyan;
                currentSection = item.Section;
                row++;
            }

            itemNum++;

            // Row #
            ws.Cell(row, 1).Value = itemNum;
            ws.Cell(row, 1).Style.Font.FontColor = XLColor.FromArgb(160, 170, 180);
            ws.Cell(row, 1).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;

            // Section (for filtering)
            ws.Cell(row, 2).Value = item.Section;
            ws.Cell(row, 2).Style.Font.FontColor = LabelColor;
            ws.Cell(row, 2).Style.Font.FontSize = 9;

            // Description
            ws.Cell(row, 3).Value = item.Description;
            ws.Cell(row, 3).Style.Alignment.WrapText = true;

            // Amount
            if (item.AmountNumeric.HasValue)
            {
                ws.Cell(row, 4).Value = item.AmountNumeric.Value;
                ws.Cell(row, 4).Style.NumberFormat.Format = "#,##0.00";
                ws.Cell(row, 4).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;
                ws.Cell(row, 4).Style.Font.FontName = "Consolas";
            }
            else if (!string.IsNullOrEmpty(item.Amount))
            {
                ws.Cell(row, 4).Value = item.Amount;
                ws.Cell(row, 4).Style.Alignment.WrapText = true;
            }

            // Unit
            ws.Cell(row, 5).Value = item.Unit;
            ws.Cell(row, 5).Style.Font.FontColor = LabelColor;

            // Notes
            ws.Cell(row, 6).Value = item.Notes;
            ws.Cell(row, 6).Style.Alignment.WrapText = true;
            ws.Cell(row, 6).Style.Font.FontColor = LabelColor;
            ws.Cell(row, 6).Style.Font.FontSize = 9;

            // Page
            if (item.PageRef > 0)
            {
                ws.Cell(row, 7).Value = item.PageRef;
                ws.Cell(row, 7).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
            }

            // Alternating row shading
            if (itemNum % 2 == 0)
                ws.Range(row, 1, row, headers.Length).Style.Fill.BackgroundColor = AltRowBg;

            // Subtle bottom border
            ws.Range(row, 1, row, headers.Length).Style.Border.BottomBorder = XLBorderStyleValues.Hair;
            ws.Range(row, 1, row, headers.Length).Style.Border.BottomBorderColor = BorderLight;

            row++;
        }

        // Auto-filter
        if (row > 2)
            ws.Range(1, 1, row - 1, headers.Length).SetAutoFilter();

        // Summary footer
        row += 2;
        ws.Cell(row, 3).Value = $"Total: {itemNum} line items extracted";
        ws.Cell(row, 3).Style.Font.Bold = true;
        ws.Cell(row, 3).Style.Font.FontColor = LabelColor;
        ws.Cell(row, 4).FormulaA1 = $"SUBTOTAL(9,D2:D{row - 2})";
        ws.Cell(row, 4).Style.NumberFormat.Format = "#,##0.00";
        ws.Cell(row, 4).Style.Font.Bold = true;
        ws.Cell(row, 4).Style.Font.FontName = "Consolas";
    }

    // ═══════════════════════════════════════════════════════════════════
    //  SHEET 3: Legal Terms
    // ═══════════════════════════════════════════════════════════════════

    private static void BuildLegalSheet(XLWorkbook wb, JsonElement root)
    {
        var ws = wb.Worksheets.Add("Legal Terms");
        int row = 1;

        // ── Term & Termination ──
        if (root.TryGetProperty("term", out var term) && term.ValueKind == JsonValueKind.Object)
        {
            row = SectionTitle(ws, row, "TERM & TERMINATION");
            row = LegalRow(ws, row, "Contract Term Type", StrFrom(term, "contract_term_type"));
            row = LegalRow(ws, row, "Term (Wells)", StrFrom(term, "contract_term_wells"));
            row = LegalRow(ws, row, "Extension Notice (Days)", StrFrom(term, "extension_notice_days"));
            row = LegalRow(ws, row, "Extension Provisions", StrFrom(term, "extension_provisions"));
            row = LegalRow(ws, row, "Early Termination — Either Party", StrFrom(term, "early_termination_by_either"));
            row = LegalRow(ws, row, "Early Termination — By Operator", StrFrom(term, "early_termination_by_operator"));
            row = LegalRow(ws, row, "Early Termination — By Contractor", StrFrom(term, "early_termination_by_contractor"));
            row = LegalRow(ws, row, "Termination Compensation", StrFrom(term, "early_termination_compensation"));
            row = LegalRow(ws, row, "Lump Sum Per Well Not Drilled", StrFrom(term, "early_termination_lump_sum_per_well"));
            row = LegalRow(ws, row, "Acceptance Deadline (Days)", StrFrom(term, "acceptance_deadline_days"));
            row++;
        }
        else
        {
            // Simplified schema — look for contract_term at root
            var ct = Str(root, "contract_term");
            if (!string.IsNullOrEmpty(ct))
            {
                row = SectionTitle(ws, row, "TERM");
                row = LegalRow(ws, row, "Contract Term", ct);
                row++;
            }
        }

        // ── Insurance ──
        if (root.TryGetProperty("insurance", out var ins) && ins.ValueKind == JsonValueKind.Object)
        {
            row = SectionTitle(ws, row, "INSURANCE REQUIREMENTS");
            row = LegalRow(ws, row, "Workers' Compensation", StrFrom(ins, "workers_comp_limit"));
            row = LegalRow(ws, row, "General Liability", StrFrom(ins, "general_liability_limit"));
            row = LegalRow(ws, row, "Auto Liability", StrFrom(ins, "auto_liability_limit"));
            row = LegalRow(ws, row, "Excess / Umbrella", StrFrom(ins, "excess_liability_limit"));
            row = LegalRow(ws, row, "Excess Notes", StrFrom(ins, "excess_liability_note"));
            row = LegalRow(ws, row, "Additional Insured Required", StrFrom(ins, "additional_insured_required"));
            row++;
        }

        // ── Indemnity ──
        if (root.TryGetProperty("indemnity", out var ind) && ind.ValueKind == JsonValueKind.Object)
        {
            row = SectionTitle(ws, row, "INDEMNITY & LIABILITY ALLOCATION");
            row = LegalRow(ws, row, "Surface Equipment Liability", StrFrom(ind, "surface_equipment_liability"));
            row = LegalRow(ws, row, "In-Hole Equipment Liability", StrFrom(ind, "inhole_equipment_liability"));
            row = LegalRow(ws, row, "Underground Damage", StrFrom(ind, "underground_damage_liability"));
            row = LegalRow(ws, row, "Pollution — Above Surface", StrFrom(ind, "pollution_above_surface"));
            row = LegalRow(ws, row, "Pollution — Below Surface", StrFrom(ind, "pollution_below_surface"));
            row = LegalRow(ws, row, "Consequential Damages", StrFrom(ind, "consequential_damages"));
            row = LegalRow(ws, row, "Control of Well", StrFrom(ind, "control_of_well"));
            row = LegalRow(ws, row, "Liability Cap", StrFrom(ind, "liability_cap_amount"));
            row++;
        }

        // ── Commencement ──
        if (root.TryGetProperty("commencement", out var comm) && comm.ValueKind == JsonValueKind.Object)
        {
            row = SectionTitle(ws, row, "COMMENCEMENT");
            row = LegalRow(ws, row, "Description", StrFrom(comm, "commencement_description"));
            row = LegalRow(ws, row, "Date", StrFrom(comm, "commencement_date"));
        }

        if (row == 1) ws.Cell(1, 1).Value = "No legal terms extracted in structured format.";

        ws.Column(1).Width = 32;
        ws.Column(2).Width = 80;
        ws.Column(2).Style.Alignment.WrapText = true;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  SHEET 4: Exhibits
    // ═══════════════════════════════════════════════════════════════════

    private static void BuildExhibitsSheet(XLWorkbook wb, JsonElement root)
    {
        var ws = wb.Worksheets.Add("Exhibits");

        if (!root.TryGetProperty("exhibits", out var exh) || exh.ValueKind != JsonValueKind.Array || exh.GetArrayLength() == 0)
        {
            ws.Cell(1, 1).Value = "No exhibits extracted.";
            return;
        }

        string[] headers = { "Exhibit", "Title", "Pages", "Summary" };
        int[] widths = { 10, 30, 12, 60 };
        for (int c = 0; c < headers.Length; c++)
        {
            var cell = ws.Cell(1, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = HeaderFg;
            cell.Style.Fill.BackgroundColor = NavyDark;
            ws.Column(c + 1).Width = widths[c];
        }
        ws.Row(1).Height = 26;

        int row = 2;
        foreach (var ex in exh.EnumerateArray())
        {
            ws.Cell(row, 1).Value = StrFrom(ex, "exhibit_id") ?? "";
            ws.Cell(row, 2).Value = StrFrom(ex, "title") ?? "";

            var ps = IntFrom(ex, "page_start");
            var pe = IntFrom(ex, "page_end");
            ws.Cell(row, 3).Value = ps > 0 && pe > 0 ? $"{ps}–{pe}" : ps > 0 ? ps.ToString() : "";

            ws.Cell(row, 4).Value = StrFrom(ex, "summary") ?? "";
            ws.Cell(row, 4).Style.Alignment.WrapText = true;

            if (row % 2 == 0)
                ws.Range(row, 1, row, headers.Length).Style.Fill.BackgroundColor = AltRowBg;

            row++;
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  SHEET 5: Signatures
    // ═══════════════════════════════════════════════════════════════════

    private static void BuildSignaturesSheet(XLWorkbook wb, JsonElement root)
    {
        var ws = wb.Worksheets.Add("Signatures");

        if (!root.TryGetProperty("signatures", out var sigs) || sigs.ValueKind != JsonValueKind.Array || sigs.GetArrayLength() == 0)
        {
            ws.Cell(1, 1).Value = "No signatures extracted.";
            return;
        }

        string[] headers = { "Party Role", "Party Name", "Signer", "Title", "Date Signed", "Status" };
        for (int c = 0; c < headers.Length; c++)
        {
            var cell = ws.Cell(1, c + 1);
            cell.Value = headers[c];
            cell.Style.Font.Bold = true;
            cell.Style.Font.FontColor = HeaderFg;
            cell.Style.Fill.BackgroundColor = NavyDark;
        }
        ws.Row(1).Height = 26;

        int row = 2;
        foreach (var sig in sigs.EnumerateArray())
        {
            ws.Cell(row, 1).Value = StrFrom(sig, "party_role") ?? "";
            ws.Cell(row, 2).Value = StrFrom(sig, "party_name") ?? "";
            ws.Cell(row, 3).Value = StrFrom(sig, "signer_name") ?? "";
            ws.Cell(row, 4).Value = StrFrom(sig, "signer_title") ?? "";
            ws.Cell(row, 5).Value = StrFrom(sig, "signature_date") ?? "";

            // Status / signed indicator
            var status = StrFrom(sig, "execution_status") ?? "";
            if (sig.TryGetProperty("signed", out var s) && s.ValueKind == JsonValueKind.True)
                status = string.IsNullOrEmpty(status) ? "Signed" : status;
            ws.Cell(row, 6).Value = status;
            ws.Cell(row, 6).Style.Font.FontColor =
                status.Contains("sign", StringComparison.OrdinalIgnoreCase) ? GreenOk : LabelColor;

            row++;
        }

        ws.Columns().AdjustToContents(1, 50);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Flatteners — comprehensive schema → RateItem list
    // ═══════════════════════════════════════════════════════════════════

    private static void FlattenCompensation(JsonElement comp, List<RateItem> items)
    {
        // Operating Rate
        if (comp.TryGetProperty("operating_rate", out var or2) && or2.ValueKind == JsonValueKind.Object)
        {
            AddRate(items, "Operating Rates", "Operating Day Rate — Without Drill Pipe",
                or2, "rate_without_drill_pipe", "per day", StrFrom(or2, "rate_notes"));
            AddRate(items, "Operating Rates", "Operating Day Rate — With Drill Pipe",
                or2, "rate_with_drill_pipe", "per day", null);
            AddRate(items, "Operating Rates", "Operating Day Rate — Using Operator's Pipe",
                or2, "rate_using_operator_pipe", "per day", null);

            var crew = IntFrom(or2, "crew_size");
            if (crew > 0)
                items.Add(new RateItem { Section = "Operating Rates", Description = "Crew Size", Amount = crew.ToString(), AmountNumeric = crew, Unit = "persons", Notes = "" });

            // Depth intervals
            if (or2.TryGetProperty("depth_interval_rates", out var di) && di.ValueKind == JsonValueKind.Array)
            {
                foreach (var d in di.EnumerateArray())
                {
                    var from = StrFrom(d, "from_depth") ?? "";
                    var to = StrFrom(d, "to_depth") ?? "";
                    AddRate(items, "Operating Rates", $"Depth {from}–{to} (Without Pipe)", d, "rate_without_pipe", "per day", null);
                    AddRate(items, "Operating Rates", $"Depth {from}–{to} (With Pipe)", d, "rate_with_pipe", "per day", null);
                }
            }
        }

        // Mobilization
        if (comp.TryGetProperty("mobilization", out var mob) && mob.ValueKind == JsonValueKind.Object)
        {
            AddDescRate(items, "Mobilization", "Initial Well Mobilization", mob, "initial_well_description");
            AddDescRate(items, "Mobilization", "Additional Wells Mobilization", mob, "additional_wells_description");
            AddRate(items, "Mobilization", "Mobilization Lump Sum", mob, "lump_sum_amount", "lump sum", null);
        }

        // Demobilization
        if (comp.TryGetProperty("demobilization", out var demob) && demob.ValueKind == JsonValueKind.Object)
        {
            AddDescRate(items, "Demobilization", "Demobilization Rate", demob, "description");
            var cond = StrFrom(demob, "conditions");
            if (!string.IsNullOrEmpty(cond))
                items.Add(new RateItem { Section = "Demobilization", Description = "Conditions", Amount = cond, Unit = "", Notes = "" });
        }

        // Repair Time
        if (comp.TryGetProperty("repair_time", out var rep) && rep.ValueKind == JsonValueKind.Object)
        {
            AddDescRate(items, "Repair Time", "Repair Time Provisions", rep, "description");
            AddRate(items, "Repair Time", "Max Hours at Operating Rate", rep, "max_hours_at_operating_rate", "hours/job", null);
            AddRate(items, "Repair Time", "Monthly Cap", rep, "monthly_cap_hours", "hours/month", null);
            AddDescRate(items, "Repair Time", "Rate After Threshold", rep, "subsequent_rate");
        }

        // Standby
        if (comp.TryGetProperty("standby", out var sby) && sby.ValueKind == JsonValueKind.Object)
            AddDescRate(items, "Standby", "Standby Time Rate", sby, "rate_description");

        // Drilling Fluids
        if (comp.TryGetProperty("drilling_fluid_rates", out var df) && df.ValueKind == JsonValueKind.Object)
        {
            AddRate(items, "Drilling Fluids", "Per Person Per Day", df, "per_person_per_day", "per person per day", null);
            AddRate(items, "Drilling Fluids", "Additional Operating Rate", df, "additional_operating_rate_per_day", "per day", null);
        }

        // Force Majeure
        AddDescRate(items, "Force Majeure", "Force Majeure Rate", comp, "force_majeure_rate");

        // Reimbursable
        AddRate(items, "Reimbursable", "Handling Fee", comp, "reimbursable_handling_fee_percent", "percent", null);

        // Revision
        if (comp.TryGetProperty("revision_in_rates", out var rev) && rev.ValueKind == JsonValueKind.Object)
        {
            AddRate(items, "Rate Revision", "Threshold", rev, "threshold_percent", "percent", StrFrom(rev, "applicable_items"));
        }

        // Personnel Rates
        if (comp.TryGetProperty("personnel_rates", out var pr) && pr.ValueKind == JsonValueKind.Array)
        {
            foreach (var p in pr.EnumerateArray())
            {
                var pos = StrFrom(p, "position") ?? "Personnel";
                AddRate(items, "Personnel Rates", pos, p, "rate_per_hour", "per hour", null);
            }
        }
    }

    private static void FlattenTechPackages(JsonElement tp, List<RateItem> items)
    {
        foreach (var pkg in tp.EnumerateArray())
        {
            var exhibitId = StrFrom(pkg, "exhibit_id") ?? "";
            var pkgName = StrFrom(pkg, "package_name") ?? "Technology Package";
            var section = string.IsNullOrEmpty(exhibitId) ? pkgName : $"Exhibit {exhibitId} — {pkgName}";

            if (pkg.TryGetProperty("line_items", out var li) && li.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in li.EnumerateArray())
                {
                    var desc = StrFrom(item, "product_service") ?? StrFrom(item, "description") ?? "";
                    if (string.IsNullOrEmpty(desc)) continue;
                    var price = StrFrom(item, "price") ?? "";
                    var unit = StrFrom(item, "unit") ?? "";

                    double? numVal = null;
                    if (double.TryParse(price.Replace(",", "").Replace("$", ""), out var parsed))
                        numVal = parsed;

                    items.Add(new RateItem
                    {
                        Section = section,
                        Description = desc,
                        Amount = price,
                        AmountNumeric = numVal,
                        Unit = unit,
                        Notes = ""
                    });
                }
            }
        }
    }

    private static void FlattenAdditionalProvisions(JsonElement ap, List<RateItem> items)
    {
        foreach (var prov in ap.EnumerateArray())
        {
            var summary = StrFrom(prov, "summary") ?? StrFrom(prov, "description") ?? "";
            if (string.IsNullOrEmpty(summary)) continue;

            var provNum = StrFrom(prov, "provision_number") ?? "";
            var desc = string.IsNullOrEmpty(provNum) ? summary : $"§{provNum} — {summary}";
            var freq = StrFrom(prov, "frequency") ?? StrFrom(prov, "unit") ?? "";
            var pageRef = IntFrom(prov, "page_ref");

            double? numVal = null;
            if (prov.TryGetProperty("amount", out var amtEl))
            {
                if (amtEl.ValueKind == JsonValueKind.Number)
                    numVal = amtEl.GetDouble();
            }

            items.Add(new RateItem
            {
                Section = "Special Provisions",
                Description = desc,
                Amount = numVal?.ToString("F2") ?? "",
                AmountNumeric = numVal,
                Unit = freq,
                Notes = StrFrom(prov, "notes") ?? "",
                PageRef = pageRef
            });
        }
    }

    private static void FlattenPaymentTerms(JsonElement pt, List<RateItem> items)
    {
        AddDescRate(items, "Payment Terms", "Payment Due", pt, "payment_due_terms");
        AddRate(items, "Payment Terms", "Disputed Invoice Window", pt, "disputed_invoice_window_days", "days", null);
        AddDescRate(items, "Payment Terms", "Late Payment Interest Rate", pt, "late_payment_interest_rate");
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Formatting helpers
    // ═══════════════════════════════════════════════════════════════════

    private static int SectionTitle(IXLWorksheet ws, int row, string title)
    {
        var range = ws.Range(row, 1, row, 2);
        range.Merge();
        ws.Cell(row, 1).Value = title;
        ws.Cell(row, 1).Style.Font.Bold = true;
        ws.Cell(row, 1).Style.Font.FontSize = 11;
        ws.Cell(row, 1).Style.Font.FontColor = SectionFg;
        ws.Cell(row, 1).Style.Fill.BackgroundColor = SectionBg;
        ws.Cell(row, 1).Style.Border.BottomBorder = XLBorderStyleValues.Thin;
        ws.Cell(row, 1).Style.Border.BottomBorderColor = NaborsCyan;
        ws.Row(row).Height = 24;
        return row + 1;
    }

    private static int SummaryRow(IXLWorksheet ws, int row, string label, string? value)
    {
        if (string.IsNullOrEmpty(value)) return row;
        ws.Cell(row, 1).Value = label;
        ws.Cell(row, 1).Style.Font.Bold = true;
        ws.Cell(row, 1).Style.Font.FontColor = LabelColor;
        ws.Cell(row, 2).Value = value;
        return row + 1;
    }

    private static int LegalRow(IXLWorksheet ws, int row, string label, string? value)
    {
        if (string.IsNullOrEmpty(value)) return row;
        ws.Cell(row, 1).Value = label;
        ws.Cell(row, 1).Style.Font.Bold = true;
        ws.Cell(row, 1).Style.Font.FontColor = LabelColor;
        ws.Cell(row, 2).Value = value;
        ws.Cell(row, 2).Style.Alignment.WrapText = true;
        ws.Range(row, 1, row, 2).Style.Border.BottomBorder = XLBorderStyleValues.Hair;
        ws.Range(row, 1, row, 2).Style.Border.BottomBorderColor = BorderLight;
        return row + 1;
    }

    // ═══════════════════════════════════════════════════════════════════
    //  JSON helpers
    // ═══════════════════════════════════════════════════════════════════

    private static string? Str(JsonElement root, string key)
    {
        if (!root.TryGetProperty(key, out var el)) return null;
        return el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number => el.TryGetInt64(out var l) ? l.ToString() : el.GetDouble().ToString("F2"),
            JsonValueKind.True => "Yes",
            JsonValueKind.False => "No",
            JsonValueKind.Null => null,
            _ => el.GetRawText()
        };
    }

    private static string? StrFrom(JsonElement el, string key)
    {
        if (el.ValueKind != JsonValueKind.Object) return null;
        return Str(el, key);
    }

    private static int IntFrom(JsonElement el, string key)
    {
        if (el.ValueKind != JsonValueKind.Object) return 0;
        if (!el.TryGetProperty(key, out var v)) return 0;
        return v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;
    }

    private static string AmountStr(JsonElement item)
    {
        if (!item.TryGetProperty("amount", out var a)) return "";
        return a.ValueKind switch
        {
            JsonValueKind.Number => a.GetDouble().ToString("F2"),
            JsonValueKind.String => a.GetString() ?? "",
            _ => ""
        };
    }

    private static double? AmountNum(JsonElement item)
    {
        if (!item.TryGetProperty("amount", out var a)) return null;
        if (a.ValueKind == JsonValueKind.Number) return a.GetDouble();
        if (a.ValueKind == JsonValueKind.String && double.TryParse(
                (a.GetString() ?? "").Replace(",", "").Replace("$", ""),
                out var d))
            return d;
        return null;
    }

    private static void AddRate(List<RateItem> items, string section, string desc,
        JsonElement parent, string key, string unit, string? notes)
    {
        if (!parent.TryGetProperty(key, out var val)) return;
        if (val.ValueKind == JsonValueKind.Null) return;

        double? num = val.ValueKind == JsonValueKind.Number ? val.GetDouble() : null;
        string amt = num?.ToString("F2") ?? val.GetString() ?? val.GetRawText();
        if (string.IsNullOrEmpty(amt) || amt == "0" || amt == "0.00") return;

        items.Add(new RateItem { Section = section, Description = desc, Amount = amt, AmountNumeric = num, Unit = unit, Notes = notes ?? "" });
    }

    private static void AddDescRate(List<RateItem> items, string section, string desc,
        JsonElement parent, string key)
    {
        var val = StrFrom(parent, key);
        if (string.IsNullOrEmpty(val)) return;

        double? num = null;
        if (double.TryParse(val.Replace(",", "").Replace("$", ""), out var d))
            num = d;

        items.Add(new RateItem { Section = section, Description = desc, Amount = val, AmountNumeric = num, Unit = "", Notes = "" });
    }

    private class RateItem
    {
        public string Section { get; set; } = "";
        public string Description { get; set; } = "";
        public string Amount { get; set; } = "";
        public double? AmountNumeric { get; set; }
        public string Unit { get; set; } = "";
        public string Notes { get; set; } = "";
        public int PageRef { get; set; }
    }
}
