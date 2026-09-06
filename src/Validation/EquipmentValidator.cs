using PdmsEquipmentLocator.Models;

namespace PdmsEquipmentLocator.Validation;

/// <summary>
/// 设备数据集验证器：解析完成后运行，输出统计与异常清单。
/// Excel/DXF 写出器与自动验证都必须基于同一份 ParseResult。
/// </summary>
public static class EquipmentValidator
{
    public class Statistics
    {
        public int TotalEquipment { get; init; }
        public int ParsedOk { get; init; }
        public int MissingPos { get; init; }
        public int InvalidPos { get; init; }
        public int MissingName { get; init; }
        public int DuplicateNames { get; init; }
        public int CoincidentXyGroups { get; init; }   // XY 完全相同的分组数
        public int CoincidentXyEquipment { get; init; } // 涉及设备台数
        public double MinX { get; init; }
        public double MaxX { get; init; }
        public double MinY { get; init; }
        public double MaxY { get; init; }
        public double MinZ { get; init; }
        public double MaxZ { get; init; }
        public bool HasCoordinate { get; init; }
    }

    /// <summary>
    /// 运行验证：重复位号、XY 重合登记为 ParseIssue；返回统计数据。
    /// </summary>
    public static Statistics Validate(ParseResult result)
    {
        var eqs = result.Equipments;

        // 重复位号
        var duplicates = eqs.GroupBy(e => e.Name, StringComparer.Ordinal)
                             .Where(g => !string.IsNullOrEmpty(g.Key) && g.Count() > 1);
        foreach (var g in duplicates)
        {
            foreach (var e in g)
            {
                result.Issues.Add(new ParseIssue
                {
                    Type = "重复位号",
                    EquipmentName = e.Name,
                    LineNumber = e.SourceLine,
                    RawContent = $"NEW EQUIPMENT /{e.Name}",
                    Description = $"位号 /{e.Name} 出现 {g.Count()} 次（行号：{string.Join(", ", g.Select(x => x.SourceLine))}）"
                });
            }
        }

        // XY 完全相同（不视为错误，仅记录）
        var coincident = eqs.Where(e => e.Position is not null)
                            .GroupBy(e => (e.Position!.X, e.Position.Y))
                            .Where(g => g.Count() > 1)
                            .ToList();
        foreach (var g in coincident)
        {
            foreach (var e in g)
            {
                result.Issues.Add(new ParseIssue
                {
                    Type = "XY坐标重合",
                    EquipmentName = e.Name,
                    LineNumber = e.SourceLine,
                    RawContent = $"POS {e.RawPosition}",
                    Description = $"设备 /{e.Name} 与 {string.Join(", ", g.Where(x => x != e).Select(x => "/" + x.Name))} 的 XY 完全相同（X={g.Key.X}, Y={g.Key.Y}）"
                });
            }
        }

        var withPos = eqs.Where(e => e.Position is not null).ToList();

        return new Statistics
        {
            TotalEquipment = eqs.Count,
            ParsedOk = eqs.Count(e => e.Status == ParseStatus.Ok),
            MissingPos = eqs.Count(e => e.Status == ParseStatus.MissingPos),
            InvalidPos = eqs.Count(e => e.Status == ParseStatus.InvalidPos),
            MissingName = eqs.Count(e => e.Status == ParseStatus.MissingName),
            DuplicateNames = duplicates.Sum(g => g.Count()),
            CoincidentXyGroups = coincident.Count,
            CoincidentXyEquipment = coincident.Sum(g => g.Count()),
            HasCoordinate = withPos.Count > 0,
            MinX = withPos.Count > 0 ? withPos.Min(e => e.Position!.X) : 0,
            MaxX = withPos.Count > 0 ? withPos.Max(e => e.Position!.X) : 0,
            MinY = withPos.Count > 0 ? withPos.Min(e => e.Position!.Y) : 0,
            MaxY = withPos.Count > 0 ? withPos.Max(e => e.Position!.Y) : 0,
            MinZ = withPos.Count > 0 ? withPos.Min(e => e.Position!.Z) : 0,
            MaxZ = withPos.Count > 0 ? withPos.Max(e => e.Position!.Z) : 0
        };
    }
}
