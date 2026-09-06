using System.Numerics;
using PdmsEquipmentLocator.Models;
using PdmsEquipmentLocator.Parser;

namespace PdmsEquipmentLocator.Tests;

/// <summary>
/// ORI 方向表达式与旋转矩阵解析测试。
/// 语法：D | D A D | D A D A D（rotate-toward）；矩阵：X = Y × Z（右手系）。
/// </summary>
public class OrientationParserTests
{
    [Theory]
    [InlineData('E', 1, 0, 0)]
    [InlineData('W', -1, 0, 0)]
    [InlineData('N', 0, 1, 0)]
    [InlineData('S', 0, -1, 0)]
    [InlineData('U', 0, 0, 1)]
    [InlineData('D', 0, 0, -1)]
    public void DirectionVector_WorldDirections(char c, float x, float y, float z)
    {
        var v = OrientationParser.DirectionVector(c);
        Assert.NotNull(v);
        Assert.Equal(new Vector3(x, y, z), v!.Value);
    }

    [Fact]
    public void DirectionVector_Invalid_ReturnsNull()
    {
        Assert.Null(OrientationParser.DirectionVector('X'));
    }

    [Theory]
    [InlineData("E")]
    [InlineData("W")]
    [InlineData("N")]
    [InlineData("S")]
    public void TryParseDirection_BasicShapes(string expr)
    {
        var ok = OrientationParser.TryParseDirection(expr, out var dir, out var err);
        Assert.True(ok, err);
        Assert.Equal(OrientationParser.DirectionVector(expr[0])!.Value, dir);
    }

    [Fact]
    public void TryParseDirection_S36W_ProducesUnitVector()
    {
        // S 朝 W 旋转 36°：绕轴 (S×W) = ((0,-1,0)×(-1,0,0)) = (0·0-0·0, 0·(-1)-0·0, 0·0-(-1)·(-1)) = (0,0,-1)
        // 旋转轴为 -Z，从 S 转向 W。
        var ok = OrientationParser.TryParseDirection("S 36 W", out var dir, out var err);
        Assert.True(ok, err);
        Assert.Equal(1f, dir.Length(), 4);
        // S 与 W 夹角 90°，旋转 36° → 与 S 夹角 36°
        Assert.Equal(MathF.Cos(36f * MathF.PI / 180f), Vector3.Dot(dir, new Vector3(0, -1, 0)), 4);
    }

    [Fact]
    public void TryParseDirection_E82U_ProducesUnitVector()
    {
        // E 朝 U 抬升 82°：与 E 夹角 82°
        var ok = OrientationParser.TryParseDirection("E 82 U", out var dir, out var err);
        Assert.True(ok, err);
        Assert.Equal(1f, dir.Length(), 4);
        Assert.Equal(MathF.Cos(82f * MathF.PI / 180f), Vector3.Dot(dir, Vector3.UnitX), 4);
        Assert.True(dir.Z > 0);
    }

    [Fact]
    public void TryParseDirection_Composite_W3_42747N60_5833U_MatchesRealData()
    {
        // 真实数据 V1011A-N3 的复合 ORI（总结与解析器文档中记录）
        var ok = OrientationParser.TryParseDirection("W 3.42747 N 60.5833 U", out var dir, out var err);
        Assert.True(ok, err);
        Assert.Equal(1f, dir.Length(), 4);
        // 仰角 60.5833°：Z 分量 ≈ sin(60.5833°)
        Assert.Equal(MathF.Sin(60.5833f * MathF.PI / 180f), dir.Z, 3);
    }

    [Fact]
    public void ParseToTransform_EmptyOrNull_ReturnsIdentity()
    {
        var r = OrientationParser.ParseToTransform(null);
        Assert.NotNull(r.Transform);
        Assert.True(r.Transform!.IsOrthonormal());
        Assert.Equal(Transform3D.Identity.XAxis, r.Transform.XAxis);
        Assert.Equal(Transform3D.Identity.YAxis, r.Transform.YAxis);
        Assert.Equal(Transform3D.Identity.ZAxis, r.Transform.ZAxis);
    }

    [Fact]
    public void ParseToTransform_Identity_YisNandZisU()
    {
        var r = OrientationParser.ParseToTransform("Y is N and Z is U");
        Assert.NotNull(r.Transform);
        // X = Y×Z = N×U = E
        Assert.Equal(Vector3.UnitX, r.Transform!.XAxis);
        Assert.Equal(Vector3.UnitY, r.Transform.YAxis);
        Assert.Equal(Vector3.UnitZ, r.Transform.ZAxis);
    }

    [Fact]
    public void ParseToTransform_Rotate90_YisEandZisU()
    {
        // Y=E, Z=U → X = E×U = S（方位角 -90°）
        var r = OrientationParser.ParseToTransform("Y is E and Z is U");
        Assert.NotNull(r.Transform);
        Assert.Equal(new Vector3(0, -1, 0), r.Transform!.XAxis);   // S
        Assert.Equal(new Vector3(1, 0, 0), r.Transform.YAxis);     // E
        Assert.Equal(new Vector3(0, 0, 1), r.Transform.ZAxis);     // U
    }

    [Fact]
    public void ParseToTransform_Rotate90_YisWandZisU()
    {
        // Y=W, Z=U → X = W×U = N（方位角 +90°）
        var r = OrientationParser.ParseToTransform("Y is W and Z is U");
        Assert.NotNull(r.Transform);
        Assert.Equal(new Vector3(0, 1, 0), r.Transform!.XAxis);    // N
        Assert.Equal(new Vector3(-1, 0, 0), r.Transform.YAxis);    // W
    }

    [Fact]
    public void ParseToTransform_Composite_OrthogonalAxes()
    {
        // X1041 复合 ORI："Y is E 10 S and Z is S 10 W 45 U"
        // 正交化后 Y·Z ≈ 0，X = Y×Z
        var r = OrientationParser.ParseToTransform("Y is E 10 S and Z is S 10 W 45 U");
        Assert.NotNull(r.Transform);
        Assert.True(r.Transform!.IsOrthonormal(1e-3f));
        // 右手系：Y = Z × X（注意不是 X × Z，后者 = -Y）
        AssertVectorClose(Vector3.Cross(r.Transform.ZAxis, r.Transform.XAxis), r.Transform.YAxis, 1e-3f);
    }

    [Fact]
    public void ParseToTransform_PumpFamily_YisDandZisN_HorizontalCylinder()
    {
        // P1081a 泵内圆柱："Y is D and Z is N" → 轴（局部 Z）沿世界 N，X = D×N = E
        var r = OrientationParser.ParseToTransform("Y is D and Z is N");
        Assert.NotNull(r.Transform);
        Assert.Equal(new Vector3(1, 0, 0), r.Transform!.XAxis);    // E
        Assert.Equal(new Vector3(0, 0, -1), r.Transform.YAxis);    // D
        Assert.Equal(new Vector3(0, 1, 0), r.Transform.ZAxis);     // N
    }

    [Fact]
    public void ParseToTransform_Malformed_ReturnsError()
    {
        var r = OrientationParser.ParseToTransform("Y is E and Z is E");   // Y 与 Z 平行
        Assert.Null(r.Transform);
        Assert.False(string.IsNullOrEmpty(r.Error));

        var r2 = OrientationParser.ParseToTransform("bogus");
        Assert.Null(r2.Transform);
        Assert.Contains("格式", r2.Error);
    }

    [Fact]
    public void RotateToward_ParallelSame_ReturnsOriginal()
    {
        var v = Vector3.UnitX;
        var r = OrientationParser.RotateToward(v, Vector3.UnitX, 30f);
        Assert.NotNull(r);
        AssertVectorClose(v, r!.Value, 1e-4f);
    }

    [Fact]
    public void RotateToward_Opposite_ReturnsNull()
    {
        var r = OrientationParser.RotateToward(Vector3.UnitX, -Vector3.UnitX, 30f);
        Assert.Null(r);
    }

    private static void AssertVectorClose(Vector3 expected, Vector3 actual, float tolerance)
    {
        Assert.True(MathF.Abs(expected.X - actual.X) <= tolerance, $"X: {expected.X} vs {actual.X}");
        Assert.True(MathF.Abs(expected.Y - actual.Y) <= tolerance, $"Y: {expected.Y} vs {actual.Y}");
        Assert.True(MathF.Abs(expected.Z - actual.Z) <= tolerance, $"Z: {expected.Z} vs {actual.Z}");
    }
}
