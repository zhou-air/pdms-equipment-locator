using ClosedXML.Excel;
using PdmsEquipmentLocator.Cad;
using PdmsEquipmentLocator.Models;

namespace PdmsEquipmentLocator.Export;

/// <summary>
/// Phase 2 报告：primitive_report.xlsx
///   Sheet1 Primitive统计    —— EQUIPMENT 层级下出现的全部对象类型、数量与支持状态
///   Sheet2 未支持Primitive  —— 尚未实现的几何原体逐条明细（禁止静默忽略）
///   Sheet3 ORI解析异常      —— 无法解析的 ORI 原始字符串逐条记录
/// 数据来源：与 Excel/DXF 完全同一份 ParseResult / OutlineRecord。
/// </summary>
public static class PrimitiveReportExporter
{
    public const string FileName = "primitive_report.xlsx";

    public static string Export(ParseResult parse, List<OutlineRecord> outlines,
        List<(string Owner, string Type, int Line, string Raw, string Error)> oriIssues, string outDir)
    {
        string path = Path.Combine(outDir, FileName);
        using var wb = new XLWorkbook();

        // ── 统计 EQUIPMENT 层级下全部对象类型 ──────────────────
        var typeCounts = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var eq in parse.Equipments)
            CountTypes(eq.Children, typeCounts);

        var ws = wb.Worksheets.Add("Primitive统计");
        ws.Cell(1, 1).Value = "Primitive类型";
        ws.Cell(1, 2).Value = "数量";
        ws.Cell(1, 3).Value = "状态";
        ws.Cell(1, 4).Value = "说明";
        StyleHeader(ws);

        int row = 2;
        foreach (var (type, count) in typeCounts.OrderByDescending(kv => kv.Value))
        {
            string status = OutlineBuilder.SupportedTypes.Contains(type) ? "已支持"
                          : OutlineBuilder.NonGeometricTypes.Contains(type) ? "非几何对象"
                          : "未支持";
            string note = status switch
            {
                "已支持" => "Phase 2A：已生成俯视轮廓",
                "非几何对象" => "容器/管件/属性类对象，不属于几何原体",
                _ => "Phase 2B 及以后实现"
            };
            ws.Cell(row, 1).Value = type;
            ws.Cell(row, 2).Value = count;
            ws.Cell(row, 3).Value = status;
            ws.Cell(row, 4).Value = note;
            row++;
        }
        FitColumns(ws, 4);

        // ── 未支持 Primitive 明细（几何原体且尚未实现） ────────
        var ws2 = wb.Worksheets.Add("未支持Primitive");
        ws2.Cell(1, 1).Value = "Equipment";
        ws2.Cell(1, 2).Value = "Parent";
        ws2.Cell(1, 3).Value = "PrimitiveType";
        ws2.Cell(1, 4).Value = "Name";
        ws2.Cell(1, 5).Value = "SourceLine";
        ws2.Cell(1, 6).Value = "RawData";
        StyleHeader(ws2);

        // 直接从层级树重新枚举（包含未生成轮廓记录的容器内部），保证完整
        var unsupported = new List<(string eq, string parent, PdmsObject node)>();
        foreach (var eq in parse.Equipments)
            CollectUnsupported(eq.Name, eq.Name, eq.Children, unsupported);
        unsupported.Sort((a, b) => a.node.SourceLine.CompareTo(b.node.SourceLine));

        row = 2;
        foreach (var (eqName, parent, node) in unsupported)
        {
            ws2.Cell(row, 1).Value = eqName;
            ws2.Cell(row, 2).Value = parent;
            ws2.Cell(row, 3).Value = node.ObjectType;
            ws2.Cell(row, 4).Value = node.Name ?? "";
            ws2.Cell(row, 5).Value = node.SourceLine;
            ws2.Cell(row, 6).Value = RawData(node);
            row++;
        }
        FitColumns(ws2, 6);

        // ── ORI 解析异常 ───────────────────────────────────────
        var ws3 = wb.Worksheets.Add("ORI解析异常");
        ws3.Cell(1, 1).Value = "对象名称";
        ws3.Cell(1, 2).Value = "对象类型";
        ws3.Cell(1, 3).Value = "TXT行号";
        ws3.Cell(1, 4).Value = "原始ORI";
        ws3.Cell(1, 5).Value = "错误说明";
        StyleHeader(ws3);

        var oriIssues2 = oriIssues
            .GroupBy(i => (i.Line, i.Raw))
            .Select(g => g.First())
            .ToList();
        row = 2;
        foreach (var issue in oriIssues2)
        {
            ws3.Cell(row, 1).Value = issue.Owner;
            ws3.Cell(row, 2).Value = issue.Type;
            ws3.Cell(row, 3).Value = issue.Line;
            ws3.Cell(row, 4).Value = issue.Raw;
            ws3.Cell(row, 5).Value = issue.Error;
            row++;
        }
        if (row == 2) ws3.Cell(2, 1).Value = "（无）";
        FitColumns(ws3, 5);

        wb.SaveAs(path);
        return path;
    }

    private static void CountTypes(List<PdmsObject> children, SortedDictionary<string, int> counts)
    {
        foreach (var c in children)
        {
            counts.TryGetValue(c.ObjectType, out int n);
            counts[c.ObjectType] = n + 1;
            CountTypes(c.Children, counts);
        }
    }

    private static void CollectUnsupported(string eqName, string parent, List<PdmsObject> children,
        List<(string eq, string parent, PdmsObject node)> sink)
    {
        foreach (var c in children)
        {
            if (!OutlineBuilder.SupportedTypes.Contains(c.ObjectType)
                && !OutlineBuilder.NonGeometricTypes.Contains(c.ObjectType))
                sink.Add((eqName, parent, c));

            var next = string.IsNullOrEmpty(c.Name) ? $"{c.ObjectType}@L{c.SourceLine}" : c.Name;
            CollectUnsupported(eqName, $"{parent}/{next}", c.Children, sink);
        }
    }

    /// <summary>对象原始数据摘要（NEW 行 + 最多 12 条属性）</summary>
    private static string RawData(PdmsObject node)
    {
        var parts = new List<string> { $"NEW {node.ObjectType} {node.Name}".TrimEnd() };
        parts.AddRange(node.Attributes.Take(12).Select(a => $"{a.Key} {a.Value}"));
        if (node.Attributes.Count > 12)
            parts.Add($"…（共 {node.Attributes.Count} 条属性）");
        return string.Join(" | ", parts);
    }

    private static void StyleHeader(IXLWorksheet ws)
    {
        var header = ws.Range(1, 1, 1, ws.LastColumnUsed()!.ColumnNumber());
        header.Style.Font.Bold = true;
        header.Style.Fill.BackgroundColor = XLColor.FromHtml("#DDEBF7");
        ws.SheetView.FreezeRows(1);
        ws.Range(1, 1, ws.LastRowUsed()!.RowNumber(), ws.LastColumnUsed()!.ColumnNumber())
          .SetAutoFilter();
    }

    private static void FitColumns(IXLWorksheet ws, int colCount)
    {
        for (int c = 1; c <= colCount; c++)
        {
            int width = c switch
            {
                1 => 14, 2 => 18, 3 => 14, 4 => 16, 5 => 10, 6 => 80,
                _ => 14
            };
            ws.Column(c).Width = width;
        }
    }
}
