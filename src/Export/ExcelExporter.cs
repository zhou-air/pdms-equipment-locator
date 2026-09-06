using ClosedXML.Excel;
using PdmsEquipmentLocator.Models;
using PdmsEquipmentLocator.Validation;

namespace PdmsEquipmentLocator.Export;

/// <summary>
/// Excel 导出器 —— 直接消费 Parser 得到的同一份 ParseResult（Equipment[] + Issues + 统计），
/// 绝不重新解析 TXT。输出 equipment_coordinates.xlsx，三个 Sheet：
///   1) 设备坐标  2) 解析异常  3) 统计
/// </summary>
public static class ExcelExporter
{
    public const string FileName = "equipment_coordinates.xlsx";

    public static string Export(ParseResult result, EquipmentValidator.Statistics stats, string outDir)
    {
        string path = Path.Combine(outDir, FileName);
        using var wb = new XLWorkbook();

        WriteEquipmentSheet(wb, result);
        WriteIssueSheet(wb, result);
        WriteStatisticsSheet(wb, stats);

        wb.SaveAs(path);
        return path;
    }

    // ── Sheet 1：设备坐标 ─────────────────────────────────────
    private static void WriteEquipmentSheet(XLWorkbook wb, ParseResult result)
    {
        var ws = wb.Worksheets.Add("设备坐标");
        string[] headers = { "序号", "设备位号", "X(mm)", "Y(mm)", "Z(mm)", "原始POS", "ORI", "所属ZONE", "TXT行号", "解析状态" };

        for (int c = 0; c < headers.Length; c++)
            ws.Cell(1, c + 1).Value = headers[c];
        ws.Range(1, 1, 1, headers.Length).Style.Font.SetBold(true);

        int row = 2;
        int idx = 0;
        foreach (var eq in result.Equipments)
        {
            idx++;
            ws.Cell(row, 1).Value = idx;
            ws.Cell(row, 2).Value = eq.Name;                     // 位号保留原始大小写
            if (eq.Position is not null)
            {
                // 真正的 Excel 数值类型，非文本；显示格式避免科学计数法但不取整
                ws.Cell(row, 3).Value = eq.Position.X;
                ws.Cell(row, 4).Value = eq.Position.Y;
                ws.Cell(row, 5).Value = eq.Position.Z;
                ws.Range(row, 3, row, 5).Style.NumberFormat.Format = "0.############";
            }
            ws.Cell(row, 6).Value = eq.RawPosition ?? string.Empty;  // 原始 POS 字符串
            ws.Cell(row, 7).Value = eq.Orientation ?? string.Empty;  // ORI 原文
            ws.Cell(row, 8).Value = eq.ZoneName;
            ws.Cell(row, 9).Value = eq.SourceLine;
            ws.Cell(row, 10).Value = eq.Status.ToString();
            row++;
        }

        if (row > 2)
            ws.Range(1, 1, row - 1, headers.Length).SetAutoFilter(); // 开启筛选
        ws.SheetView.FreezeRows(1);                                 // 冻结首行

        ws.Column(1).Width = 8;
        ws.Column(2).Width = 18;
        ws.Column(3).Width = 15;
        ws.Column(4).Width = 15;
        ws.Column(5).Width = 15;
        ws.Column(6).Width = 42;
        ws.Column(7).Width = 42;
        ws.Column(8).Width = 38;
        ws.Column(9).Width = 12;
        ws.Column(10).Width = 14;
    }

    // ── Sheet 2：解析异常 ─────────────────────────────────────
    private static void WriteIssueSheet(XLWorkbook wb, ParseResult result)
    {
        var ws = wb.Worksheets.Add("解析异常");
        string[] headers = { "异常类型", "设备位号", "TXT行号", "原始内容", "说明" };

        for (int c = 0; c < headers.Length; c++)
            ws.Cell(1, c + 1).Value = headers[c];
        ws.Range(1, 1, 1, headers.Length).Style.Font.SetBold(true);

        int row = 2;
        foreach (var issue in result.Issues)
        {
            ws.Cell(row, 1).Value = issue.Type;
            ws.Cell(row, 2).Value = issue.EquipmentName ?? string.Empty;
            ws.Cell(row, 3).Value = issue.LineNumber;
            ws.Cell(row, 4).Value = issue.RawContent;
            ws.Cell(row, 5).Value = issue.Description;
            row++;
        }

        if (row > 2)
            ws.Range(1, 1, row - 1, headers.Length).SetAutoFilter();
        ws.SheetView.FreezeRows(1);

        ws.Column(1).Width = 16;
        ws.Column(2).Width = 18;
        ws.Column(3).Width = 12;
        ws.Column(4).Width = 44;
        ws.Column(5).Width = 70;
    }

    // ── Sheet 3：统计 ─────────────────────────────────────────
    private static void WriteStatisticsSheet(XLWorkbook wb, EquipmentValidator.Statistics stats)
    {
        var ws = wb.Worksheets.Add("统计");
        ws.Cell(1, 1).Value = "指标";
        ws.Cell(1, 2).Value = "数值";
        ws.Range(1, 1, 1, 2).Style.Font.SetBold(true);

        var rows = new List<(string Label, object? Value)>
        {
            ("EQUIPMENT 总数", stats.TotalEquipment),
            ("成功解析数量", stats.ParsedOk),
            ("缺失POS数量", stats.MissingPos),
            ("解析失败数量", stats.InvalidPos),
            ("重复位号数量", stats.DuplicateNames),
            ("XY完全重合设备数量", stats.CoincidentXyEquipment),
            ("XY完全重合分组数", stats.CoincidentXyGroups),
        };
        if (stats.HasCoordinate)
        {
            rows.Add(("X最小值(mm)", stats.MinX));
            rows.Add(("X最大值(mm)", stats.MaxX));
            rows.Add(("Y最小值(mm)", stats.MinY));
            rows.Add(("Y最大值(mm)", stats.MaxY));
            rows.Add(("Z最小值(mm)", stats.MinZ));
            rows.Add(("Z最大值(mm)", stats.MaxZ));
        }
        else
        {
            rows.Add(("坐标范围", "（无成功解析坐标）"));
        }

        int row = 2;
        foreach (var (label, value) in rows)
        {
            ws.Cell(row, 1).Value = label;
            switch (value)
            {
                case double d:
                    ws.Cell(row, 2).Value = d;
                    ws.Cell(row, 2).Style.NumberFormat.Format = "0.############";
                    break;
                case int i:
                    ws.Cell(row, 2).Value = i;
                    break;
                case string s:
                    ws.Cell(row, 2).Value = s;
                    break;
                default:
                    ws.Cell(row, 2).Value = value?.ToString() ?? string.Empty;
                    break;
            }
            row++;
        }

        ws.SheetView.FreezeRows(1);
        ws.Column(1).Width = 26;
        ws.Column(2).Width = 22;
    }
}
