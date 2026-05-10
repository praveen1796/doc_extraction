using System.Text.Json;
using ClosedXML.Excel;
using DocumentExtractionService.Core.Models;
using DocumentExtractionService.Core.Services;

namespace DocumentExtractionService.Api.Services;

/// <summary>
/// Multi-sheet Excel export for well_plan extraction JSON — readable tables.
/// </summary>
public static class WellPlanExcelExporter
{
    public static ExportResult Export(ExtractionResponse result)
    {
        if (result.Data is null)
            throw new InvalidOperationException("Cannot export to Excel: extraction result has no data.");

        var root = result.Data.RootElement;
        using var workbook = new XLWorkbook();

        AddSummarySheet(workbook, root);
        AddApprovalsSheet(workbook, root);
        AddWellsOverviewSheet(workbook, root);
        AddFormationsSheet(workbook, root);
        AddCasingSheet(workbook, root);
        AddDrillingSectionsSheet(workbook, root);
        AddFluidsSheet(workbook, root);
        AddRisksSheet(workbook, root);

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        var safeFileName = ExportFileNameHelper.Sanitize(result.Metadata.SourceFile);

        return new ExportResult
        {
            FileBytes = ms.ToArray(),
            ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            FileName = $"{safeFileName}_well_plan.xlsx"
        };
    }

    private static string Sheet(string name) =>
        name.Length <= 31 ? name : name[..31];

    private static void AddSummarySheet(XLWorkbook wb, JsonElement root)
    {
        var ws = wb.Worksheets.Add(Sheet("Summary"));
        var keys = new[]
        {
            "rig_name", "operator", "pad_name", "location", "report_date", "report_status",
            "depth_unit", "document_format", "language", "document_type"
        };
        var r = 1;
        foreach (var key in keys)
        {
            if (!root.TryGetProperty(key, out var el)) continue;
            ws.Cell(r, 1).Value = key;
            ws.Cell(r, 1).Style.Font.Bold = true;
            ws.Cell(r, 2).Value = JsonScalarToString(el);
            r++;
        }

        if (root.TryGetProperty("wells", out var wells) && wells.ValueKind == JsonValueKind.Array)
        {
            ws.Cell(r, 1).Value = "well_count";
            ws.Cell(r, 1).Style.Font.Bold = true;
            ws.Cell(r, 2).Value = wells.GetArrayLength();
        }

        ws.Columns(1, 2).AdjustToContents();
    }

    private static void AddApprovalsSheet(XLWorkbook wb, JsonElement root)
    {
        var ws = wb.Worksheets.Add(Sheet("Approvals"));
        if (!root.TryGetProperty("approvals", out var arr) || arr.ValueKind != JsonValueKind.Array || arr.GetArrayLength() == 0)
        {
            ws.Cell(1, 1).Value = "(none)";
            return;
        }

        var headers = new[] { "name", "action", "datetime" };
        WriteHeaderRow(ws, headers, 1);
        var r = 2;
        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            for (var c = 0; c < headers.Length; c++)
                ws.Cell(r, c + 1).Value = item.TryGetProperty(headers[c], out var v) ? JsonScalarToString(v) : "";
            r++;
        }
        ws.RangeUsed()?.SetAutoFilter();
        ws.Columns().AdjustToContents();
    }

    private static void AddWellsOverviewSheet(XLWorkbook wb, JsonElement root)
    {
        var ws = wb.Worksheets.Add(Sheet("Wells_overview"));
        if (!root.TryGetProperty("wells", out var wells) || wells.ValueKind != JsonValueKind.Array)
        {
            ws.Cell(1, 1).Value = "(no wells)";
            return;
        }

        var headers = new[]
        {
            "well_name", "well_type", "api_number", "afe_number", "operator_well_id", "permit_id",
            "target_formation", "design", "total_depth_md", "total_depth_tvd", "lateral_length",
            "surface_coordinates", "coordinate_system", "ground_level", "rkb", "skid_order"
        };
        WriteHeaderRow(ws, headers, 1);
        var r = 2;
        foreach (var well in wells.EnumerateArray())
        {
            if (well.ValueKind != JsonValueKind.Object) continue;
            for (var c = 0; c < headers.Length; c++)
                ws.Cell(r, c + 1).Value = well.TryGetProperty(headers[c], out var v) ? JsonScalarToString(v) : "";
            r++;
        }
        ws.RangeUsed()?.SetAutoFilter();
        ws.Columns().AdjustToContents();
    }

    private static void AddFormationsSheet(XLWorkbook wb, JsonElement root)
    {
        var ws = wb.Worksheets.Add(Sheet("Formations"));
        if (!root.TryGetProperty("wells", out var wells) || wells.ValueKind != JsonValueKind.Array)
        {
            ws.Cell(1, 1).Value = "(no wells)";
            return;
        }

        var headers = new[] { "well_name", "formation_name", "md", "tvd" };
        WriteHeaderRow(ws, headers, 1);
        var r = 2;
        foreach (var well in wells.EnumerateArray())
        {
            if (well.ValueKind != JsonValueKind.Object) continue;
            var wn = well.TryGetProperty("well_name", out var wname) ? wname.GetString() ?? "" : "";
            if (!well.TryGetProperty("formation_tops", out var tops) || tops.ValueKind != JsonValueKind.Array)
                continue;
            foreach (var row in tops.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Object) continue;
                ws.Cell(r, 1).Value = wn;
                ws.Cell(r, 2).Value = row.TryGetProperty("formation_name", out var fn) ? fn.GetString() ?? "" : "";
                ws.Cell(r, 3).Value = row.TryGetProperty("md", out var md) ? JsonNumberOrString(md) : "";
                ws.Cell(r, 4).Value = row.TryGetProperty("tvd", out var tvd) ? JsonNumberOrString(tvd) : "";
                r++;
            }
        }

        if (r == 2) ws.Cell(1, 1).Value = "(no formation tops)";
        else
        {
            ws.RangeUsed()?.SetAutoFilter();
            ws.Columns().AdjustToContents();
        }
    }

    private static void AddCasingSheet(XLWorkbook wb, JsonElement root)
    {
        var ws = wb.Worksheets.Add(Sheet("Casing"));
        if (!root.TryGetProperty("wells", out var wells) || wells.ValueKind != JsonValueKind.Array)
        {
            ws.Cell(1, 1).Value = "(no wells)";
            return;
        }

        var headers = new[]
        {
            "well_name", "section_name", "hole_size", "casing_od", "casing_id", "grade",
            "weight_per_length", "connection", "start_md", "end_md", "cement_type", "cement_details"
        };
        WriteHeaderRow(ws, headers, 1);
        var r = 2;
        foreach (var well in wells.EnumerateArray())
        {
            if (well.ValueKind != JsonValueKind.Object) continue;
            var wn = well.TryGetProperty("well_name", out var wname) ? wname.GetString() ?? "" : "";
            if (!well.TryGetProperty("casing_program", out var rows) || rows.ValueKind != JsonValueKind.Array)
                continue;
            foreach (var row in rows.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Object) continue;
                ws.Cell(r, 1).Value = wn;
                for (var c = 1; c < headers.Length; c++)
                    ws.Cell(r, c + 1).Value = GetWellChildScalar(row, headers[c], CasingAlt(headers[c]));
                r++;
            }
        }

        if (r == 2) ws.Cell(1, 1).Value = "(no casing)";
        else
        {
            ws.RangeUsed()?.SetAutoFilter();
            ws.Columns().AdjustToContents();
        }
    }

    private static string? CasingAlt(string key) => key switch
    {
        "start_md" => "start_md_ft",
        "end_md" => "end_md_ft",
        "hole_size" => "hole_size_in",
        "casing_od" => "casing_od_in",
        "weight_per_length" => "weight_lbm_ft",
        _ => null
    };

    private static void AddDrillingSectionsSheet(XLWorkbook wb, JsonElement root)
    {
        var ws = wb.Worksheets.Add(Sheet("Drilling_sections"));
        if (!root.TryGetProperty("wells", out var wells) || wells.ValueKind != JsonValueKind.Array)
        {
            ws.Cell(1, 1).Value = "(no wells)";
            return;
        }

        var headers = new[]
        {
            "well_name", "section_name", "hole_size", "depth_from", "depth_to", "interval",
            "wob", "rpm", "flow_rate", "rop", "diffp_max", "bha_type", "primary_bit", "backup_bit", "comments"
        };
        WriteHeaderRow(ws, headers, 1);
        var r = 2;
        foreach (var well in wells.EnumerateArray())
        {
            if (well.ValueKind != JsonValueKind.Object) continue;
            var wn = well.TryGetProperty("well_name", out var wname) ? wname.GetString() ?? "" : "";
            if (!well.TryGetProperty("drilling_sections", out var rows) || rows.ValueKind != JsonValueKind.Array)
                continue;
            foreach (var row in rows.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Object) continue;
                ws.Cell(r, 1).Value = wn;
                for (var c = 1; c < headers.Length; c++)
                    ws.Cell(r, c + 1).Value = GetWellChildScalar(row, headers[c], DrillingAlt(headers[c]));
                r++;
            }
        }

        if (r == 2) ws.Cell(1, 1).Value = "(no drilling sections)";
        else
        {
            ws.RangeUsed()?.SetAutoFilter();
            ws.Columns().AdjustToContents();
        }
    }

    private static string? DrillingAlt(string key) => key switch
    {
        "depth_from" => "depth_from_ft",
        "depth_to" => "depth_to_ft",
        "hole_size" => "hole_size_in",
        "flow_rate" => "flow_rate_gpm",
        "rop" => "rop_fth",
        "diffp_max" => "diffp_max_psi",
        "wob" => "wob_klbf",
        _ => null
    };

    private static XLCellValue GetWellChildScalar(JsonElement row, string primary, string? alternate)
    {
        if (row.TryGetProperty(primary, out var v)) return JsonScalarToString(v);
        if (alternate != null && row.TryGetProperty(alternate, out v)) return JsonScalarToString(v);
        return "";
    }

    private static void AddFluidsSheet(XLWorkbook wb, JsonElement root)
    {
        var ws = wb.Worksheets.Add(Sheet("Fluids"));
        if (!root.TryGetProperty("wells", out var wells) || wells.ValueKind != JsonValueKind.Array)
        {
            ws.Cell(1, 1).Value = "(no wells)";
            return;
        }

        var headers = new[] { "well_name", "section", "fluid_type", "design_mw", "min_mw", "max_mw", "min_fit", "mudloggers", "comments" };
        WriteHeaderRow(ws, headers, 1);
        var r = 2;
        foreach (var well in wells.EnumerateArray())
        {
            if (well.ValueKind != JsonValueKind.Object) continue;
            var wn = well.TryGetProperty("well_name", out var wname) ? wname.GetString() ?? "" : "";
            if (!well.TryGetProperty("drilling_fluids", out var rows) || rows.ValueKind != JsonValueKind.Array)
                continue;
            foreach (var row in rows.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Object) continue;
                ws.Cell(r, 1).Value = wn;
                for (var c = 1; c < headers.Length; c++)
                    ws.Cell(r, c + 1).Value = row.TryGetProperty(headers[c], out var v) ? JsonScalarToString(v) : "";
                r++;
            }
        }

        if (r == 2) ws.Cell(1, 1).Value = "(no fluids)";
        else
        {
            ws.RangeUsed()?.SetAutoFilter();
            ws.Columns().AdjustToContents();
        }
    }

    private static void AddRisksSheet(XLWorkbook wb, JsonElement root)
    {
        var ws = wb.Worksheets.Add(Sheet("Risks"));
        if (!root.TryGetProperty("wells", out var wells) || wells.ValueKind != JsonValueKind.Array)
        {
            ws.Cell(1, 1).Value = "(no wells)";
            return;
        }

        var headers = new[] { "well_name", "section", "risk", "comments" };
        WriteHeaderRow(ws, headers, 1);
        var r = 2;
        foreach (var well in wells.EnumerateArray())
        {
            if (well.ValueKind != JsonValueKind.Object) continue;
            var wn = well.TryGetProperty("well_name", out var wname) ? wname.GetString() ?? "" : "";
            if (!well.TryGetProperty("risks_and_hazards", out var rows) || rows.ValueKind != JsonValueKind.Array)
                continue;
            foreach (var row in rows.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Object) continue;
                ws.Cell(r, 1).Value = wn;
                ws.Cell(r, 2).Value = row.TryGetProperty("section", out var s) ? s.GetString() ?? "" : "";
                ws.Cell(r, 3).Value = row.TryGetProperty("risk", out var rk) ? rk.GetString() ?? "" : "";
                ws.Cell(r, 4).Value = row.TryGetProperty("comments", out var cm) ? cm.GetString() ?? "" : "";
                r++;
            }
        }

        if (r == 2) ws.Cell(1, 1).Value = "(no risks)";
        else
        {
            ws.RangeUsed()?.SetAutoFilter();
            ws.Columns().AdjustToContents();
        }
    }

    private static void WriteHeaderRow(IXLWorksheet ws, string[] headers, int row)
    {
        for (var i = 0; i < headers.Length; i++)
        {
            ws.Cell(row, i + 1).Value = headers[i];
            ws.Cell(row, i + 1).Style.Font.Bold = true;
            ws.Cell(row, i + 1).Style.Fill.BackgroundColor = XLColor.LightBlue;
        }
    }

    private static XLCellValue JsonScalarToString(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.String => el.GetString() ?? "",
        JsonValueKind.Number => el.TryGetInt64(out var l) ? l : el.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => Blank.Value,
        _ => el.GetRawText()
    };

    private static XLCellValue JsonNumberOrString(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.Number => el.TryGetInt64(out var l) ? l : el.GetDouble(),
        JsonValueKind.String => el.GetString() ?? "",
        JsonValueKind.Null => Blank.Value,
        _ => el.GetRawText()
    };
}

internal static class ExportFileNameHelper
{
    private static readonly char[] InvalidFileNameChars = Path.GetInvalidFileNameChars();

    public static string Sanitize(string fileName)
    {
        if (string.IsNullOrEmpty(fileName)) return "export";
        var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
        var sanitized = new string(nameWithoutExt
            .Select(c => InvalidFileNameChars.Contains(c) ? '_' : c)
            .ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "export" : sanitized;
    }
}
