using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
using PdmsEquipmentLocator.Models;

namespace PdmsEquipmentLocator.Parser;

/// <summary>
/// PDMS ORI 解析器（Phase 2）：把 "ORI Y is ... and Z is ..." 转换为旋转 Transform3D。
///
/// 方向表达式语法（经真实文件 X1041 / V1011A-N3 等复合 ORI 验证，Y·Z ≈ 0）：
///   简单方向:   "E" / "W" / "N" / "S" / "U" / "D"
///   方位角复合:  "S 36 W"        = 从 S 朝 W 旋转 36°（水平面内）
///   仰角复合:    "E 82 U"        = 从 E 向 U 抬升 82°
///   全复合:      "W 3.42747 N 60.5833 U" = 先方位角（W 朝 N 3.42747°），再仰角（朝 U 60.5833°）
///   即统一语法:  D ( A D )*
///
/// 旋转矩阵约定（右手系）：ORI 给出局部 Y、Z 轴在父坐标系中的方向，
/// X = Y × Z；默认（无 ORI）X=E, Y=N, Z=U。
/// 已验证：P1081a/P1083a/P1052 同模板泵组在不同设备级旋转下内部几何一致。
/// </summary>
public static class OrientationParser
{
    /// <summary>
    /// ORI 解析结果。Transform 为 null 时 Error 说明原因（不得静默忽略）。
    /// </summary>
    public readonly record struct OrientationOutcome(Transform3D? Transform, string Raw, string Error);

    private static readonly Regex OriRegex =
        new(@"^Y\s+is\s+(?<y>.+?)\s+and\s+Z\s+is\s+(?<z>.+?)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>六个世界方向字母 → 单位向量</summary>
    public static Vector3? DirectionVector(char c) => c switch
    {
        'E' or 'e' => Vector3.UnitX,
        'W' or 'w' => -Vector3.UnitX,
        'N' or 'n' => Vector3.UnitY,
        'S' or 's' => -Vector3.UnitY,
        'U' or 'u' => Vector3.UnitZ,
        'D' or 'd' => -Vector3.UnitZ,
        _ => null
    };

    /// <summary>
    /// 解析方向表达式（不含 "Y is"/"Z is" 前缀）为单位向量。
    /// 语法: D 或 D A D 或 D A D A D（更多段也按 (A D) 重复处理）。
    /// </summary>
    public static bool TryParseDirection(string expr, out Vector3 direction, out string error)
    {
        direction = Vector3.Zero;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(expr))
        {
            error = "方向表达式为空";
            return false;
        }

        var tokens = expr.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries
                                               | StringSplitOptions.TrimEntries);

        // 单个方向字母
        if (tokens.Length == 1)
        {
            if (tokens[0].Length == 1 && DirectionVector(tokens[0][0]) is { } v)
            {
                direction = v;
                return true;
            }
            error = $"无法识别的方向表达式: \"{expr}\"";
            return false;
        }

        // D (A D)* 序列：首 token 必须是方向字母
        if (tokens[0].Length != 1 || DirectionVector(tokens[0][0]) is not { } cur)
        {
            error = $"方向表达式必须以方向字母开头: \"{expr}\"";
            return false;
        }

        int i = 1;
        while (i < tokens.Length)
        {
            // 需要 (角度, 方向) 成对出现
            if (i + 1 >= tokens.Length)
            {
                error = $"方向表达式格式不完整（缺少成对的角度+方向）: \"{expr}\"";
                return false;
            }

            if (!double.TryParse(tokens[i], NumberStyles.Float, CultureInfo.InvariantCulture, out double angleDeg))
            {
                error = $"角度 \"{tokens[i]}\" 无法解析为数值: \"{expr}\"";
                return false;
            }

            if (tokens[i + 1].Length != 1 || DirectionVector(tokens[i + 1][0]) is not { } target)
            {
                error = $"旋转目标 \"{tokens[i + 1]}\" 不是有效方向字母: \"{expr}\"";
                return false;
            }

            var rotated = RotateToward(cur, target, (float)angleDeg);
            if (rotated is null)
            {
                error = $"无法从 {Describe(cur)} 朝 {tokens[i + 1]} 旋转 {angleDeg}°（方向平行或相反）: \"{expr}\"";
                return false;
            }

            cur = rotated.Value;
            i += 2;
        }

        direction = Vector3.Normalize(cur);
        return true;
    }

    /// <summary>
    /// 把方向 v 绕「v×target」轴朝 target 旋转 angleDeg 度。
    /// v 与 target 平行同向 → 原样；反向 → 无法定义（返回 null）。
    /// </summary>
    public static Vector3? RotateToward(Vector3 v, Vector3 target, float angleDeg)
    {
        float rad = angleDeg * MathF.PI / 180f;
        var axis = Vector3.Cross(v, target);
        float axisLen = axis.Length();

        if (axisLen < 1e-6f)
            return Vector3.Dot(v, target) > 0 ? v : null;

        axis /= axisLen;
        return Vector3.Normalize(v * MathF.Cos(rad) + Vector3.Cross(axis, v) * MathF.Sin(rad));
    }

    /// <summary>
    /// 解析完整 ORI 值（如 "Y is E and Z is U"）为旋转 Transform3D（平移为零）。
    /// </summary>
    public static OrientationOutcome ParseToTransform(string? oriValue)
    {
        if (string.IsNullOrWhiteSpace(oriValue))
            return new OrientationOutcome(Transform3D.Identity, oriValue ?? string.Empty, string.Empty);

        var m = OriRegex.Match(oriValue);
        if (!m.Success)
            return new OrientationOutcome(null, oriValue,
                $"ORI 不符合 \"Y is ... and Z is ...\" 格式: \"{oriValue}\"");

        if (!TryParseDirection(m.Groups["y"].Value, out var yDir, out string yErr))
            return new OrientationOutcome(null, oriValue, $"ORI Y 轴解析失败: {yErr}");

        if (!TryParseDirection(m.Groups["z"].Value, out var zDir, out string zErr))
            return new OrientationOutcome(null, oriValue, $"ORI Z 轴解析失败: {zErr}");

        // 正交化：Y 保持，Z 去除 Y 分量（Data Listing 数值舍入可能带来微小不正交）
        yDir = Vector3.Normalize(yDir);
        float dotYZ = Vector3.Dot(yDir, zDir);
        if (MathF.Abs(dotYZ) > 0.99f)
            return new OrientationOutcome(null, oriValue,
                $"ORI Y 轴与 Z 轴近乎平行（点积 {dotYZ:0.####}）: \"{oriValue}\"");

        zDir = Vector3.Normalize(zDir - yDir * dotYZ);

        var xDir = Vector3.Normalize(Vector3.Cross(yDir, zDir)); // X = Y × Z（右手系）
        return new OrientationOutcome(
            Transform3D.FromAxes(xDir, yDir, zDir, Vector3.Zero),
            oriValue, string.Empty);
    }

    private static string Describe(Vector3 v) =>
        $"({v.X:0.###},{v.Y:0.###},{v.Z:0.###})";
}
