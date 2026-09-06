using ClosedXML.Excel;
using PdmsEquipmentLocator.Export;
using PdmsEquipmentLocator.Models;

namespace PdmsEquipmentLocator.Tests;

/// <summary>
/// primitive_report.xlsx 测试：未支持 Primitive 必须逐条列出（禁止静默忽略）。
/// </summary>
public class PrimitiveReportTests
{
    [Fact]
    public void Export_UnsupportedCone_ListedInReport()
    {
        var cone = new PdmsObject { ObjectType = "CONE", Name = "cone1", SourceLine = 40 };
        cone.Attributes.Add(("DTOP", "100mm"));
        cone.Attributes.Add(("DBOT", "200mm"));
        cone.Attributes.Add(("HEIG", "300mm"));

        var box = new BoxPrimitive { ObjectType = "BOX", Name = "B1", SourceLine = 50 };
        box.Attributes.Add(("XLEN", "100mm"));
        box.Attributes.Add(("YLEN", "100mm"));
        box.Attributes.Add(("ZLEN", "100mm"));

        var eq = new Equipment { Name = "E1", Position = new Position3D(0, 0, 0), SourceLine = 1 };
        eq.Children.Add(cone);
        eq.Children.Add(box);

        var parse = new ParseResult();
        parse.Equipments.Add(eq);

        var builder = new Cad.OutlineBuilder();
        var outlines = builder.Build(parse);
        Assert.Single(outlines);   // 只有 BOX 生成轮廓

        string dir = Path.Combine(Path.GetTempPath(), "pdms-report-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            string path = PrimitiveReportExporter.Export(parse, outlines, builder.OriIssues, dir);
            using var wb = new XLWorkbook(path);

            var stats = wb.Worksheet("Primitive统计");
            var coneRow = stats.RowsUsed().FirstOrDefault(r =>
                string.Equals(r.Cell(1).GetString(), "CONE", StringComparison.OrdinalIgnoreCase));
            Assert.NotNull(coneRow);
            Assert.Equal("未支持", coneRow!.Cell(3).GetString());

            var unsupported = wb.Worksheet("未支持Primitive");
            var found = unsupported.RowsUsed().Any(r =>
                r.Cell(3).GetString() == "CONE" && r.Cell(1).GetString() == "E1");
            Assert.True(found, "未支持Primitive 明细中应包含 CONE 记录");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
