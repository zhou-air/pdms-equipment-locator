using PdmsEquipmentLocator.Cad;
using PdmsEquipmentLocator.Models;

namespace PdmsEquipmentLocator.Tests;

/// <summary>
/// OutlineBuilder 集成测试：BOX / CYLINDER 几何 → 世界变换 → XY 投影 → 凸包。
/// 覆盖用户规范用例：1000×500 无旋转 / 旋转 90° → 500×1000 / 父级平移 / 层级语义。
/// </summary>
public class OutlineBuilderTests
{
    // ── 夹具 ──────────────────────────────────────────────────
    private static BoxPrimitive Box(double x, double y, double z,
        string? pos = null, string? ori = null, string? name = null)
    {
        var b = new BoxPrimitive
        {
            ObjectType = "BOX", Name = name, RawPosition = pos, Orientation = ori, SourceLine = 10
        };
        b.Attributes.Add(("XLEN", F(x)));
        b.Attributes.Add(("YLEN", F(y)));
        b.Attributes.Add(("ZLEN", F(z)));
        return b;
    }

    private static CylinderPrimitive Cyl(double diam, double heig,
        string? pos = null, string? ori = null, string? name = null)
    {
        var c = new CylinderPrimitive
        {
            ObjectType = "CYLINDER", Name = name, RawPosition = pos, Orientation = ori, SourceLine = 20
        };
        c.Attributes.Add(("DIAM", F(diam)));
        c.Attributes.Add(("HEIG", F(heig)));
        return c;
    }

    private static PdmsObject Sub(string? pos = null, string? ori = null, params PdmsObject[] children)
    {
        var s = new PdmsObject
        {
            ObjectType = "SUBEQUIPMENT", RawPosition = pos, Orientation = ori, SourceLine = 30
        };
        foreach (var c in children) s.Children.Add(c);
        return s;
    }

    private static Equipment Eq(string name, double x, double y, double z,
        string? ori = null, params PdmsObject[] children)
    {
        var e = new Equipment
        {
            Name = name, Position = new Position3D(x, y, z), Orientation = ori, SourceLine = 1
        };
        foreach (var c in children) e.Children.Add(c);
        return e;
    }

    private static ParseResult Result(params Equipment[] eqs)
    {
        var r = new ParseResult();
        foreach (var e in eqs) r.Equipments.Add(e);
        return r;
    }

    private static string F(double v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture) + "mm";

    private static (float minX, float maxX, float minY, float maxY) Bounds(OutlineRecord rec)
        => (rec.Hull.Min(p => p.X), rec.Hull.Max(p => p.X),
            rec.Hull.Min(p => p.Y), rec.Hull.Max(p => p.Y));

    // ── BOX ───────────────────────────────────────────────────
    [Fact]
    public void Box_Unrotated_HullIs1000x500_AtEquipmentPos()
    {
        // 用户规范用例：XLEN 1000 / YLEN 500，无旋转 → 凸包 1000×500
        var parse = Result(Eq("E1", 1000, 2000, 500, null, Box(1000, 500, 300, name: "B1")));
        var outlines = new OutlineBuilder().Build(parse);

        var rec = Assert.Single(outlines);
        Assert.Equal("OK", rec.Status);
        Assert.Equal(4, rec.Hull.Count);
        var (minX, maxX, minY, maxY) = Bounds(rec);
        Assert.Equal(500f, minX, 3);      // 1000 ± 500
        Assert.Equal(1500f, maxX, 3);
        Assert.Equal(1750f, minY, 3);     // 2000 ± 250
        Assert.Equal(2250f, maxY, 3);
        Assert.Equal(350d, rec.WorldZMin, 3);   // 500 ± 150
        Assert.Equal(650d, rec.WorldZMax, 3);
    }

    [Fact]
    public void Box_Rotated90_HullBecomes500x1000()
    {
        // 用户规范用例：1000×500 旋转 90°（ORI "Y is E and Z is U"）→ 500×1000
        var parse = Result(Eq("E1", 0, 0, 0, "Y is E and Z is U", Box(1000, 500, 100, name: "B1")));
        var outlines = new OutlineBuilder().Build(parse);

        var rec = Assert.Single(outlines);
        Assert.Equal("OK", rec.Status);
        var (minX, maxX, minY, maxY) = Bounds(rec);
        Assert.Equal(-250f, minX, 3);     // 宽 500
        Assert.Equal(250f, maxX, 3);
        Assert.Equal(-500f, minY, 3);     // 高 1000
        Assert.Equal(500f, maxY, 3);
    }

    [Fact]
    public void Box_WithParentChain_TranslationsCompose()
    {
        // 设备 (1000,2000,0) → SUBEQUIPMENT POS "E 100mm" → BOX POS "N 50mm"
        // 盒中心世界 = (1100, 2050, 0)；XLEN 200 → X∈[1000,1200]，YLEN 100 → Y∈[2000,2100]
        var parse = Result(Eq("E1", 1000, 2000, 0, null,
            Sub("E 100mm N 0mm U 0mm", null, Box(200, 100, 50, pos: "E 0mm N 50mm U 0mm", name: "B1"))));
        var outlines = new OutlineBuilder().Build(parse);

        var rec = Assert.Single(outlines);
        Assert.Equal("OK", rec.Status);
        var (minX, maxX, minY, maxY) = Bounds(rec);
        Assert.Equal(1000f, minX, 3);
        Assert.Equal(1200f, maxX, 3);
        Assert.Equal(2000f, minY, 3);
        Assert.Equal(2100f, maxY, 3);
    }

    [Fact]
    public void ChildPos_UnderRotatedParent_InterpretedInParentLocalFrame()
    {
        // 语义关键用例：设备绕 Z 90°（Y is E and Z is U），子 BOX POS "E 100mm"
        // 子局部 +X 在设备系为 E，但设备旋转后 E → 世界 -Y：
        // 子世界中心 = R·(100,0,0) = (0,-100,0)
        var parse = Result(Eq("E1", 0, 0, 0, "Y is E and Z is U",
            Box(10, 10, 10, pos: "E 100mm N 0mm U 0mm", name: "B1")));
        var outlines = new OutlineBuilder().Build(parse);

        var rec = Assert.Single(outlines);
        Assert.Equal("OK", rec.Status);
        var (minX, maxX, minY, maxY) = Bounds(rec);
        Assert.Equal(-5f, minX, 3);
        Assert.Equal(5f, maxX, 3);
        Assert.Equal(-105f, minY, 3);     // 中心 (0,-100) ± 5
        Assert.Equal(-95f, maxY, 3);
    }

    // ── CYLINDER ──────────────────────────────────────────────
    [Fact]
    public void Cylinder_Vertical_HullIs32GonRadius500()
    {
        var parse = Result(Eq("E1", 0, 0, 0, null, Cyl(1000, 2000, name: "C1")));
        var outlines = new OutlineBuilder().Build(parse);

        var rec = Assert.Single(outlines);
        Assert.Equal("OK", rec.Status);
        Assert.Equal(32, rec.Hull.Count);
        var (minX, maxX, minY, maxY) = Bounds(rec);
        Assert.Equal(-500f, minX, 3);
        Assert.Equal(500f, maxX, 3);
        Assert.Equal(-500f, minY, 3);
        Assert.Equal(500f, maxY, 3);
        Assert.Equal(-1000d, rec.WorldZMin, 3);   // 2000 ± 1000
        Assert.Equal(1000d, rec.WorldZMax, 3);
    }

    [Fact]
    public void Cylinder_Horizontal_LiesAlongWorldN()
    {
        // 泵组语义：ORI "Y is D and Z is N" → 轴沿世界 N，横截面在 X-U 平面
        // DIAM 1000（r=500）→ X∈[-500,500], Z∈[-500,500]；HEIG 2000 → Y∈[-1000,1000]
        var parse = Result(Eq("E1", 0, 0, 0, null, Cyl(1000, 2000, ori: "Y is D and Z is N", name: "C1")));
        var outlines = new OutlineBuilder().Build(parse);

        var rec = Assert.Single(outlines);
        Assert.Equal("OK", rec.Status);
        var (minX, maxX, minY, maxY) = Bounds(rec);
        Assert.Equal(-500f, minX, 3);
        Assert.Equal(500f, maxX, 3);
        Assert.Equal(-1000f, minY, 3);     // 长度沿 Y
        Assert.Equal(1000f, maxY, 3);
        Assert.Equal(-500d, rec.WorldZMin, 3);
        Assert.Equal(500d, rec.WorldZMax, 3);
    }

    [Fact]
    public void Cylinder_EquipmentRotated90_StillCircle()
    {
        // 立式圆柱绕自身轴旋转，XY 投影不变（应仍为半径 500 的圆）
        var parse = Result(Eq("E1", 0, 0, 0, "Y is E and Z is U", Cyl(1000, 2000, name: "C1")));
        var outlines = new OutlineBuilder().Build(parse);

        var rec = Assert.Single(outlines);
        Assert.Equal("OK", rec.Status);
        var (minX, maxX, minY, maxY) = Bounds(rec);
        Assert.Equal(-500f, minX, 3);
        Assert.Equal(500f, maxX, 3);
        Assert.Equal(-500f, minY, 3);
        Assert.Equal(500f, maxY, 3);
    }

    // ── 异常路径 ──────────────────────────────────────────────
    [Fact]
    public void UnsupportedGeometricType_NoOutlineRecord_ReportedSeparately()
    {
        // CONE 等未支持几何类型不产生轮廓记录（由 primitive_report 明细列出，禁止静默忽略）
        var cone = new PdmsObject { ObjectType = "CONE", Name = "cone1", SourceLine = 40 };
        cone.Attributes.Add(("DTOP", "100mm"));
        cone.Attributes.Add(("DBOT", "200mm"));
        var parse = Result(Eq("E1", 0, 0, 0, null, cone));
        var outlines = new OutlineBuilder().Build(parse);
        Assert.Empty(outlines);
    }

    [Fact]
    public void EquipmentBadOri_DescendantsMarkedOriFailed()
    {
        var builder = new OutlineBuilder();
        var parse = Result(Eq("E1", 0, 0, 0, "bogus", Box(100, 100, 100, name: "B1")));
        var outlines = builder.Build(parse);

        var rec = Assert.Single(outlines);
        Assert.Equal("ORI解析失败", rec.Status);
        Assert.Single(builder.OriIssues);
        Assert.Empty(rec.Hull);
    }

    [Fact]
    public void EquipmentNoPosition_DescendantsMarkedNoPos()
    {
        var e = Eq("E1", 0, 0, 0, null, Box(100, 100, 100, name: "B1"));
        e.Position = null;
        var parse = Result(e);
        var outlines = new OutlineBuilder().Build(parse);

        var rec = Assert.Single(outlines);
        Assert.Equal("设备无坐标", rec.Status);
        Assert.Empty(rec.Hull);
    }

    [Fact]
    public void BoxMissingParams_StatusParameterMissing()
    {
        var box = new BoxPrimitive { ObjectType = "BOX", Name = "B1", SourceLine = 50 };
        box.Attributes.Add(("XLEN", "100mm"));   // 缺 YLEN/ZLEN
        var parse = Result(Eq("E1", 0, 0, 0, null, box));
        var outlines = new OutlineBuilder().Build(parse);

        var rec = Assert.Single(outlines);
        Assert.Equal("参数缺失", rec.Status);
        Assert.Contains("YLEN", rec.StatusNote);
        Assert.Empty(rec.Hull);
    }

    [Fact]
    public void DebugMode_PopulatesWorldVertices()
    {
        var builder = new OutlineBuilder { DebugMode = true };
        var parse = Result(Eq("E1", 0, 0, 0, null, Box(100, 100, 100, name: "B1")));
        var outlines = builder.Build(parse);

        var rec = Assert.Single(outlines);
        Assert.Equal(8, rec.WorldVertices.Count);   // BOX 8 角点全部保留
        Assert.Equal(rec.WorldOrigin, new System.Numerics.Vector3(0, 0, 0));
        Assert.Equal(32, new OutlineBuilder { DebugMode = false }.Build(
            Result(Eq("E1", 0, 0, 0, null, Cyl(100, 200, name: "C1"))))[0].Hull.Count);
    }

    [Fact]
    public void EquipmentTransforms_Exposed_ForDebugMatrixDump()
    {
        var builder = new OutlineBuilder();
        var parse = Result(Eq("E1", 10, 20, 30, "Y is E and Z is U", Box(100, 100, 100, name: "B1")));
        builder.Build(parse);

        Assert.True(builder.EquipmentTransforms.TryGetValue("E1", out var t));
        Assert.Equal(new System.Numerics.Vector3(10, 20, 30), t.Translation);
        Assert.Equal(new System.Numerics.Vector3(0, -1, 0), t.XAxis);   // S
        Assert.Equal(new System.Numerics.Vector3(1, 0, 0), t.YAxis);    // E
    }
}
