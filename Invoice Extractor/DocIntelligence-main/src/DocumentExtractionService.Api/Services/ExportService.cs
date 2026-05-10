using System.Text.Json;
using ClosedXML.Excel;
using DocumentExtractionService.Core.Models;
using DocumentExtractionService.Core.Services;

namespace DocumentExtractionService.Api.Services;

/// <summary>
/// Export service implementation supporting JSON and Excel formats.
/// Uses ClosedXML for Excel generation.
///
/// Document-type-specific exporters:
///   - well_plan → WellPlanExcelExporter (multi-sheet: wells, formations, casing)
///   - contract  → ContractExcelExporter (multi-sheet: summary, rates table, signatures)
///   - (default) → Generic JSON flattener with array sections
/// </summary>
public class ExportService : IExportService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    public ExportResult Export(ExtractionResponse result, ExportFormat format)
    {
        return format switch
        {
            ExportFormat.Json => ExportJson(result),
            ExportFormat.Excel => ExportExcel(result),
            _ => throw new NotSupportedException($"Export format '{format}' is not supported.")
        };
    }

    // ── JSON export ──────────────────────────────────────────────────

    private static ExportResult ExportJson(ExtractionResponse result)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(result, JsonOptions);
        var safeFileName = ExportFileNameHelper.Sanitize(result.Metadata.SourceFile);

        return new ExportResult
        {
            FileBytes = json,
            ContentType = "application/json",
            FileName = $"{safeFileName}_extracted.json"
        };
    }

    // ── Excel export ─────────────────────────────────────────────────

    private static ExportResult ExportExcel(ExtractionResponse result)
    {
        if (result.Data is null)
        {
            throw new InvalidOperationException(
                "Cannot export to Excel: the extraction result contains no data. " +
                "This usually means the extraction failed.");
        }

        // ── Dispatch to document-type-specific exporters ──
        var docType = result.DocumentType?.ToLowerInvariant() ?? "";

        if (docType == "well_plan")
            return WellPlanExcelExporter.Export(result);

        if (docType == "contract")
            return ContractExcelExporter.Export(result);

        // ── Default generic exporter ──
        return GenericExcelExport(result);
    }

    private static ExportResult GenericExcelExport(ExtractionResponse result)
    {
        using var workbook = new XLWorkbook();
        var sheetName = string.IsNullOrEmpty(result.DocumentType)
            ? "Extraction"
            : result.DocumentType.Replace("_", " ");

        if (sheetName.Length > 31) sheetName = sheetName[..31];

        var worksheet = workbook.Worksheets.Add(sheetName);
        WriteJsonToSheet(worksheet, result.Data!.RootElement);
        worksheet.Columns().AdjustToContents(1, 100);

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        var safeFileName = ExportFileNameHelper.Sanitize(result.Metadata.SourceFile);

        return new ExportResult
        {
            FileBytes = ms.ToArray(),
            ContentType = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            FileName = $"{safeFileName}_extracted.xlsx"
        };
    }

    /// <summary>
    /// Flatten a JSON object into a two-row sheet: header row + value row.
    /// Arrays get a separate section below.
    /// </summary>
    private static void WriteJsonToSheet(IXLWorksheet ws, JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object) return;

        int col = 1;
        int arrayStartRow = 4;

        foreach (var property in root.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.Array)
            {
                arrayStartRow = WriteArraySection(ws, property.Name, property.Value, arrayStartRow);
                arrayStartRow += 2;
                continue;
            }

            if (property.Value.ValueKind == JsonValueKind.Object) continue;

            ws.Cell(1, col).Value = property.Name;
            ws.Cell(1, col).Style.Font.Bold = true;
            ws.Cell(2, col).Value = GetCellValue(property.Value);
            col++;
        }
    }

    private static int WriteArraySection(
        IXLWorksheet ws, string sectionName, JsonElement array, int startRow)
    {
        if (array.GetArrayLength() == 0) return startRow;

        ws.Cell(startRow, 1).Value = sectionName;
        ws.Cell(startRow, 1).Style.Font.Bold = true;
        ws.Cell(startRow, 1).Style.Font.FontSize = 12;
        startRow++;

        var firstItem = array[0];
        if (firstItem.ValueKind != JsonValueKind.Object)
        {
            foreach (var item in array.EnumerateArray())
            {
                ws.Cell(startRow, 1).Value = GetCellValue(item);
                startRow++;
            }
            return startRow;
        }

        var headers = firstItem.EnumerateObject()
            .Where(p => p.Value.ValueKind != JsonValueKind.Object &&
                        p.Value.ValueKind != JsonValueKind.Array)
            .Select(p => p.Name)
            .ToList();

        for (int i = 0; i < headers.Count; i++)
        {
            ws.Cell(startRow, i + 1).Value = headers[i];
            ws.Cell(startRow, i + 1).Style.Font.Bold = true;
        }
        startRow++;

        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;

            for (int i = 0; i < headers.Count; i++)
            {
                if (item.TryGetProperty(headers[i], out var value))
                    ws.Cell(startRow, i + 1).Value = GetCellValue(value);
            }
            startRow++;
        }

        return startRow;
    }

    private static XLCellValue GetCellValue(JsonElement element)
    {
        return element.ValueKind switch
        {
            JsonValueKind.String => element.GetString() ?? "",
            JsonValueKind.Number => element.TryGetInt64(out var l) ? l : element.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => Blank.Value,
            _ => element.GetRawText()
        };
    }
}
