using System.Text.RegularExpressions;
using PdmsEquipmentLocator.Models;

namespace PdmsEquipmentLocator.Parser;

/// <summary>
/// POS 行解析器：把 "POS E 71700mm N 3505mm U 2021mm" 转换为世界坐标（mm）。
/// E=+X W=-X N=+Y S=-Y U=+Z D=-Z
/// 严格保留原始数值精度，不做任何"合理化"取整。
/// </summary>
public static class PositionParser
{
    // 匹配一个分量：方向字母 + 数值 + mm。允许整数与小数（含负号，虽然 PDMS 通常用 W/S/D 表达负方向）。
    private static readonly Regex AxisRegex =
        new(@"(?<dir>[ENWSUD])\s+(?<val>-?\d+(?:\.\d+)?)\s*mm", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    // 完整三分量校验（用于严格检查分量个数）
    private static readonly Regex ThreeAxisRegex =
        new(@"^(?<x>[ENWSUD])\s+-?\d+(?:\.\d+)?mm\s+(?<y>[ENWSUD])\s+-?\d+(?:\.\d+)?mm\s+(?<z>[ENWSUD])\s+-?\d+(?:\.\d+)?mm\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>
    /// 解析结果
    /// </summary>
    public readonly record struct ParseOutcome(Position3D? Position, string Error);

    /// <summary>
    /// 解析一行 POS 属性值（不含 "POS" 关键字本身）。
    /// </summary>
    public static ParseOutcome Parse(string posValue)
    {
        if (string.IsNullOrWhiteSpace(posValue))
            return new ParseOutcome(null, "POS 内容为空");

        if (!ThreeAxisRegex.IsMatch(posValue))
        {
            // 尝试提取已有分量，给出精确的错误描述
            var axes = AxisRegex.Matches(posValue);
            if (axes.Count == 0)
                return new ParseOutcome(null, $"POS 无法识别任何坐标分量: \"{posValue}\"");
            if (axes.Count < 3)
                return new ParseOutcome(null, $"POS 坐标分量不足（{axes.Count}/3）: \"{posValue}\"");
            return new ParseOutcome(null, $"POS 格式无法解析: \"{posValue}\"");
        }

        double x = 0, y = 0, z = 0;
        var seen = new HashSet<char>();

        foreach (Match m in AxisRegex.Matches(posValue))
        {
            var dir = char.ToUpperInvariant(m.Groups["dir"].Value[0]);
            var val = double.Parse(m.Groups["val"].Value, System.Globalization.CultureInfo.InvariantCulture);

            if (!seen.Add(dir))
                return new ParseOutcome(null, $"POS 方向分量重复（{dir}）: \"{posValue}\"");

            switch (dir)
            {
                case 'E': x = +val; break;
                case 'W': x = -val; break;
                case 'N': y = +val; break;
                case 'S': y = -val; break;
                case 'U': z = +val; break;
                case 'D': z = -val; break;
            }
        }

        return new ParseOutcome(new Position3D(x, y, z), string.Empty);
    }
}
